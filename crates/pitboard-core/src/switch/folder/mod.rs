#[cfg(test)]
mod tests;
mod verdict;

use super::enroll::{Enrolled, account, claim};
use super::{Error, Outcome, Result, Settled, enrolled, identify, shape};
use crate::context::Context;
use crate::error::{LoginDoubt, Then};
use crate::host::Listed;
use crate::in_use::{self, Doubt, InUse, Known};
use crate::provider::folder::{AppSettings, Folder};
use crate::service::{Permit, Warning};
use crate::state::{self, Key, SetAside, State, new_id};
use std::path::Path;
use verdict::{Named, Unsure, Verdict, look};

pub(crate) enum Open {
    No,
    Yes(Vec<Listed>),
    // why the process list did not read
    Unknown(String),
}

pub(crate) fn open(ctx: &Context, app: &dyn Folder) -> Open {
    let root = match app.root(ctx) {
        Ok(root) => root,
        Err(e) => return Open::Unknown(e.to_string()),
    };
    let all = match ctx.host().listing() {
        Ok(all) => all,
        Err(why) => return Open::Unknown(why),
    };
    let running: Vec<Listed> = all
        .iter()
        .filter(|one| app.is_the_app(ctx, &root, one, &all))
        .cloned()
        .collect();
    if running.is_empty() {
        Open::No
    } else {
        Open::Yes(running)
    }
}

// The app seen closed, and its data folder held, until this is dropped.
pub(crate) struct Closed {
    lock: Option<std::fs::File>,
}

impl Drop for Closed {
    // Unlocked, not only closed: a child forked meanwhile holds a copy, as in `SignIn::drop`.
    fn drop(&mut self) {
        if let Some(lock) = &self.lock {
            let _ = lock.unlock();
        }
    }
}

pub(crate) fn closed(ctx: &Context, app: &'static dyn Folder) -> Result<Closed> {
    let root = app.root(ctx).map_err(|e| shape(app.id(), e))?;
    quiet(ctx, app)?;
    hold(app, &root)
}

fn quiet(ctx: &Context, app: &dyn Folder) -> Result<()> {
    match open(ctx, app) {
        Open::No => Ok(()),
        Open::Yes(listed) => Err(Error::AppOpen {
            app: app.app_name(),
            still: still(app, &listed),
        }),
        Open::Unknown(why) => Err(Error::AppStateUnknown {
            app: app.app_name(),
            why,
        }),
    }
}

// Nothing where the app's own program runs; otherwise each part of it that does.
fn still(app: &dyn Folder, listed: &[Listed]) -> Vec<String> {
    let main = Path::new("Contents/MacOS").join(app.app_name());
    if listed.iter().any(|one| one.path.ends_with(&main)) {
        return Vec::new();
    }
    listed
        .iter()
        .map(|one| {
            let name = one.path.file_name().unwrap_or(one.path.as_os_str());
            format!("{} (pid {})", name.to_string_lossy(), one.pid)
        })
        .collect()
}

// Only "not found" says a path is not there: any other error is a fact not read, so doubt.
fn there(tool: crate::provider::ProviderId, path: &Path) -> Result<bool> {
    match std::fs::symlink_metadata(path) {
        Ok(_) => Ok(true),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(e) => Err(Error::LoginFolderUnknown {
            tool,
            what: format!("could not be looked at ({e})"),
            then: Then::Nothing,
        }),
    }
}

// Never waited for. A folder that is not there holds nothing to move, so nothing is held.
fn hold(app: &dyn Folder, root: &Path) -> Result<Closed> {
    if !there(app.id(), root)? {
        return Ok(Closed { lock: None });
    }
    match crate::host::fs::lock_dir(root) {
        Ok(lock) => Ok(Closed { lock: Some(lock) }),
        Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => Err(Error::LoginFolderBusy {
            app: app.app_name(),
        }),
        Err(e) => Err(Error::LiveStoreUnsupported {
            tool: app.id(),
            reason: format!("{}'s data folder could not be held ({e})", app.app_name()),
        }),
    }
}

// A read's answer, from one read of the settings, which are all it opens.
pub(crate) fn read(ctx: &Context, state: &State, app: &dyn Folder) -> (Known, Option<Key>) {
    match app.root(ctx) {
        Ok(root) => judged(state, app, app.settings(&root).ok().as_ref()),
        Err(_) => {
            let known = Known {
                last: state.in_use(app.id()).cloned(),
                doubt: None,
                own_record: None,
            };
            (known, None)
        }
    }
}

// The record judged against the settings, `None` where they did not read, and who looks signed out.
pub(crate) fn judged(
    state: &State,
    app: &dyn Folder,
    settings: Option<&AppSettings>,
) -> (Known, Option<Key>) {
    let tool = app.id();
    let last = state.in_use(tool).cloned();
    let Some(settings) = settings else {
        let known = Known {
            last,
            doubt: Some(Doubt::LoginUnreadable),
            own_record: None,
        };
        return (known, None);
    };
    let named = settings.named(app).map(in_use::named_only);
    (
        in_use::judged_by(tool, last, named),
        looks_signed_out(state, app, settings),
    )
}

// The account recorded in use, where the settings name it beside caches a Log out leaves.
fn looks_signed_out(state: &State, app: &dyn Folder, settings: &AppSettings) -> Option<Key> {
    let owner = state.in_use(app.id())?.owner.as_ref()?;
    if settings.named(app)? != owner.account_uuid {
        return None;
    }
    let keys = settings.account_keys(app);
    let emptied = app
        .keys()
        .iter()
        .filter(|key| **key != app.naming_key())
        .all(|key| keys.get(key).is_some_and(|value| app.emptied(key, value)));
    if !emptied {
        return None;
    }
    state
        .account_of(app.id(), owner)
        .map(crate::state::Account::key)
}

// Moves nothing. A park the enrolment supersedes is set aside, never deleted.
pub(crate) fn enroll(
    settled: Settled,
    key: &Key,
    app: &'static dyn Folder,
) -> Result<(Enrolled, Vec<Warning>)> {
    let Settled {
        _exclusive,
        mut state,
        ctx,
        permit,
    } = settled;
    let tool = key.provider;
    let _closed = closed(&ctx, app)?;
    let (named, login, aside, taken_as_named) = match look(&ctx, &state, app)? {
        Verdict::Nobody => return Err(Error::LiveCredentialAbsent { tool }),
        Verdict::Account {
            key: holder,
            session,
        } => (Named::Enrolled(holder), session.login, None, false),
        Verdict::Unsure(Unsure::Unfinished) => return Err(Error::SignInUnfinished { tool }),
        Verdict::Unsure(Unsure::RecordedUnderAnother { named, .. }) => {
            let login = state
                .in_use(tool)
                .map(|record| record.login.clone())
                .unwrap_or_default();
            (named, login, None, true)
        }
        Verdict::Unsure(Unsure::AlsoParked {
            key: holder,
            session,
        }) => (Named::Enrolled(holder), session.login, None, false),
        Verdict::Unsure(Unsure::Unseen { named, session }) => (named, session.login, None, false),
        Verdict::Unsure(Unsure::HeldTwice {
            named,
            session,
            also_in,
        }) => (named, session.login, Some(also_in), false),
    };
    let owner = match &named {
        Named::Enrolled(holder) => enrolled(&state, holder)?.owner(),
        Named::NotEnrolled { uuid } => in_use::named_only(uuid.clone()),
    };
    claim(&state, key, &owner)?;
    let now = ctx.now();
    let parked = state.get(key).and_then(|a| a.parked.clone());
    state.upsert(account(
        tool,
        state.id_of(tool, &owner),
        &key.label,
        &owner,
        parked,
        Some(now),
        None,
    ));
    let mut warnings = Vec::new();
    if taken_as_named {
        warnings.push(Warning::TakenAsNamed {
            tool,
            label: state.typed(key),
        });
    }
    let superseded = |state: &State| -> Vec<Key> {
        state
            .accounts
            .iter()
            .filter(|a| a.provider() == tool)
            .filter(|a| {
                a.parked.as_ref().is_some_and(|park| {
                    Some(&park.service) == aside.as_ref() || (a.is(key) && park.login != login)
                })
            })
            .map(crate::state::Account::key)
            .collect()
    };
    for holder in superseded(&state) {
        let Some(park) = state.get(&holder).and_then(|a| a.parked.clone()) else {
            continue;
        };
        let path = crate::store::folder::at(&ctx, &park.service);
        state.set_aside(&holder, park, SetAside::Superseded, now);
        warnings.push(Warning::SetAside {
            tool,
            path,
            label: Some(state.typed(&holder)),
            why: SetAside::Superseded,
        });
    }
    let found = InUse {
        named: Some(new_id(tool, &owner)),
        owner: Some(owner),
        login,
        known_at: now,
    };
    let (_, noticed) = identify::record(&mut state, tool, found, now);
    state::save(&ctx, permit, &state)?;
    identify::write_down(&ctx, permit, &noticed);
    Ok((
        Enrolled::Current {
            email: String::new(),
        },
        warnings,
    ))
}

// `use` of the account the app holds, which moves nothing; this Pitboard parks no folder.
pub(crate) fn switch(
    settled: Settled,
    key: &Key,
    app: &'static dyn Folder,
) -> Result<(Outcome, Vec<Warning>)> {
    let Settled {
        _exclusive,
        state,
        ctx,
        ..
    } = settled;
    let tool = key.provider;
    enrolled(&state, key)?;
    let _closed = closed(&ctx, app)?;
    let root = app.root(&ctx).map_err(|e| shape(tool, e))?;
    if !there(tool, &root)? {
        return Err(Error::LoginFolderUnknown {
            tool,
            what: "does not exist yet".into(),
            then: Then::OpenAndQuit,
        });
    }
    match look(&ctx, &state, app)? {
        Verdict::Account { key: holder, .. } if holder == *key => Ok((
            Outcome::AlreadyActive {
                label: state.typed(key),
                config_updated: false,
            },
            Vec::new(),
        )),
        Verdict::Unsure(unsure) => Err(refusal(&state, tool, &unsure)),
        Verdict::Nobody | Verdict::Account { .. } => Err(Error::NothingParked {
            tool,
            label: state.typed(key),
        }),
    }
}

// For a button drawn while `key`'s account was in use: nothing to write while it still is.
pub(crate) fn confirm(
    settled: Settled,
    key: &Key,
    app: &'static dyn Folder,
) -> Result<(Outcome, Vec<Warning>)> {
    let Settled {
        _exclusive,
        state,
        ctx,
        ..
    } = settled;
    enrolled(&state, key)?;
    let _closed = closed(&ctx, app)?;
    match look(&ctx, &state, app)? {
        Verdict::Account { key: holder, .. } if holder == *key => Ok((
            Outcome::AlreadyActive {
                label: state.typed(key),
                config_updated: false,
            },
            Vec::new(),
        )),
        _ => Err(Error::AccountNotInUse {
            tool: key.provider,
            label: state.typed(key),
        }),
    }
}

// A refusal is `forget`'s to take by its doubt rule. Signed out is recorded as nobody.
pub(crate) fn in_use_for_forget(
    ctx: &Context,
    permit: Permit,
    state: &mut State,
    key: &Key,
    app: &'static dyn Folder,
) -> Result<(bool, Closed)> {
    let closed = closed(ctx, app)?;
    match look(ctx, state, app)? {
        Verdict::Account { key: holder, .. } => Ok((holder == *key, closed)),
        Verdict::Nobody => {
            let now = ctx.now();
            let nobody = InUse {
                owner: None,
                login: String::new(),
                known_at: now,
                named: in_use::named(ctx, key.provider),
            };
            let (_, noticed) = identify::record(state, key.provider, nobody, now);
            state::save(ctx, permit, state)?;
            identify::write_down(ctx, permit, &noticed);
            Ok((false, closed))
        }
        Verdict::Unsure(unsure) => Err(refusal(state, key.provider, &unsure)),
    }
}

// What every change but `enroll` says of a login only the person's word can file.
fn refusal(state: &State, tool: crate::provider::ProviderId, unsure: &Unsure) -> Error {
    let named = |named: &Named| match named {
        Named::Enrolled(key) => Some(state.typed(key)),
        Named::NotEnrolled { .. } => None,
    };
    let doubt = |why| Error::LoginInDoubt { tool, why };
    match unsure {
        Unsure::Unfinished => Error::SignInUnfinished { tool },
        Unsure::RecordedUnderAnother {
            recorded,
            named: by,
        } => doubt(LoginDoubt::NamedAnother {
            recorded: state.typed(recorded),
            named: named(by),
        }),
        Unsure::AlsoParked { key, .. }
        | Unsure::Unseen {
            named: Named::Enrolled(key),
            ..
        } => doubt(LoginDoubt::SignedInOutside {
            label: state.typed(key),
        }),
        Unsure::Unseen {
            named: Named::NotEnrolled { .. },
            ..
        } => Error::LiveAccountNotEnrolled {
            tool,
            who: crate::words::login(tool, "", false),
        },
        Unsure::HeldTwice {
            named: by, also_in, ..
        } => {
            let holder = state
                .accounts
                .iter()
                .find(|a| a.parked.as_ref().is_some_and(|p| p.service == *also_in))
                .or_else(|| {
                    state
                        .aside
                        .iter()
                        .find(|aside| aside.name == *also_in)
                        .and_then(|aside| state.owner_of_park(aside.account.as_deref()?))
                });
            doubt(LoginDoubt::NamedAnother {
                recorded: holder.map_or_else(|| also_in.clone(), |a| state.typed(&a.key())),
                named: named(by),
            })
        }
    }
}
