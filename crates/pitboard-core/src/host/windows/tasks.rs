//! Task Scheduler through `ITaskService`, on a thread of its own in COM's multithreaded
//! apartment, whatever apartment the caller's thread is in. A build for tests asks it nothing.

use crate::context::Context;
use crate::error::{Error, Result};
use crate::host::task::{FOLDER, Registered, Service};
use crate::service::Permit;
use windows::Win32::Foundation::{E_FAIL, RPC_E_CHANGED_MODE};
use windows::Win32::System::Com::{
    CLSCTX_INPROC_SERVER, COINIT_MULTITHREADED, CoCreateInstance, CoInitializeEx, CoUninitialize,
};
use windows::Win32::System::TaskScheduler::{
    ITaskFolder, ITaskService, TASK_CREATE_OR_UPDATE, TASK_LOGON_INTERACTIVE_TOKEN, TaskScheduler,
};
use windows::Win32::System::Variant::VARIANT;
use windows::core::BSTR;

#[derive(Debug)]
pub(super) struct System;

// HRESULT_FROM_WIN32 of ERROR_FILE_NOT_FOUND and ERROR_PATH_NOT_FOUND.
const NOT_FOUND: [u32; 2] = [0x8007_0002, 0x8007_0003];
// ERROR_ALREADY_EXISTS: another process made the folder first.
const ALREADY_THERE: u32 = 0x8007_00B7;

const ASKED_IN_A_TEST: &str = "a build for tests asked Task Scheduler itself";

impl Service for System {
    fn read(&self, _ctx: &Context, name: &str) -> Option<Registered> {
        if cfg!(any(test, feature = "test-support")) {
            return None;
        }
        let name = name.to_owned();
        in_the_mta(move |service| {
            let Some(folder) = folder(service)? else {
                return Ok(None);
            };
            // SAFETY: a folder the connected service gave, and the task it gave.
            unsafe {
                let task = match folder.GetTask(&BSTR::from(name.as_str())) {
                    Ok(task) => task,
                    Err(e) if not_found(&e) => return Ok(None),
                    Err(e) => return Err(e),
                };
                Ok(Some(Registered {
                    xml: task.Xml()?.to_string(),
                }))
            }
        })
        .ok()
        .flatten()
    }

    // TASK_CREATE_OR_UPDATE with an interactive token and no user: the person registering it.
    fn register(&self, _: Permit, _ctx: &Context, name: &str, xml: &str) -> Result<()> {
        if cfg!(any(test, feature = "test-support")) {
            return Err(Error::ScheduleRefused {
                detail: ASKED_IN_A_TEST.into(),
            });
        }
        let (name, xml) = (name.to_owned(), xml.to_owned());
        in_the_mta(move |service| {
            let folder = made_folder(service)?;
            // SAFETY: a folder the connected service gave.
            unsafe {
                folder.RegisterTask(
                    &BSTR::from(name.as_str()),
                    &BSTR::from(xml.as_str()),
                    TASK_CREATE_OR_UPDATE.0,
                    &VARIANT::default(),
                    &VARIANT::default(),
                    TASK_LOGON_INTERACTIVE_TOKEN,
                    &VARIANT::default(),
                )
            }
            .map(drop)
        })
        .map_err(refused)
    }

    fn delete(&self, _: Permit, _ctx: &Context, name: &str) -> Result<()> {
        if cfg!(any(test, feature = "test-support")) {
            return Err(Error::ScheduleRefused {
                detail: ASKED_IN_A_TEST.into(),
            });
        }
        let name = name.to_owned();
        in_the_mta(move |service| {
            let Some(folder) = folder(service)? else {
                return Ok(());
            };
            // SAFETY: a folder the connected service gave, and its root.
            unsafe {
                match folder.DeleteTask(&BSTR::from(name.as_str()), 0) {
                    Err(e) if !not_found(&e) => return Err(e),
                    _ => {}
                }
                // Refused while another person's task is in it.
                let _ = service
                    .GetFolder(&BSTR::from(r"\"))
                    .and_then(|root| root.DeleteFolder(&BSTR::from(&FOLDER[1..]), 0));
            }
            Ok(())
        })
        .map_err(refused)
    }
}

fn refused(e: windows::core::Error) -> Error {
    Error::ScheduleRefused {
        detail: format!("Task Scheduler said {e}"),
    }
}

fn not_found(e: &windows::core::Error) -> bool {
    NOT_FOUND.contains(&(e.code().0 as u32))
}

fn in_the_mta<T: Send>(
    work: impl FnOnce(&ITaskService) -> windows::core::Result<T> + Send,
) -> windows::core::Result<T> {
    std::thread::scope(|scope| {
        scope
            .spawn(|| {
                // SAFETY: this thread's own apartment, left below where this call joined it.
                let joined = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
                if joined.is_err() && joined != RPC_E_CHANGED_MODE {
                    return Err(joined.into());
                }
                let done = connected().and_then(|service| work(&service));
                if joined.is_ok() {
                    // SAFETY: after every interface this thread made is released.
                    unsafe { CoUninitialize() };
                }
                done
            })
            .join()
            .unwrap_or_else(|_| Err(E_FAIL.into()))
    })
}

fn connected() -> windows::core::Result<ITaskService> {
    // SAFETY: COM is initialised on this thread, which alone uses the service.
    unsafe {
        let service: ITaskService = CoCreateInstance(&TaskScheduler, None, CLSCTX_INPROC_SERVER)?;
        service.Connect(
            &VARIANT::default(),
            &VARIANT::default(),
            &VARIANT::default(),
            &VARIANT::default(),
        )?;
        Ok(service)
    }
}

fn folder(service: &ITaskService) -> windows::core::Result<Option<ITaskFolder>> {
    // SAFETY: a connected service.
    match unsafe { service.GetFolder(&BSTR::from(FOLDER)) } {
        Ok(folder) => Ok(Some(folder)),
        Err(e) if not_found(&e) => Ok(None),
        Err(e) => Err(e),
    }
}

// RF-E1: a standard user may make `\Pitboard\`.
fn made_folder(service: &ITaskService) -> windows::core::Result<ITaskFolder> {
    if let Some(folder) = folder(service)? {
        return Ok(folder);
    }
    // SAFETY: a connected service, and the root folder it gave.
    let made = unsafe {
        service
            .GetFolder(&BSTR::from(r"\"))?
            .CreateFolder(&BSTR::from(&FOLDER[1..]), &VARIANT::default())
    };
    match made {
        Err(e) if e.code().0 as u32 == ALREADY_THERE => match folder(service)? {
            Some(folder) => Ok(folder),
            None => Err(e),
        },
        made => made,
    }
}
