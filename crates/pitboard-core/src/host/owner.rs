// Whose a process is on Windows, decided on every system from what Windows said of it.

#[cfg(any(windows, test))]
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Told {
    User(String),
    Refused,
    Gone,
}

#[cfg(any(windows, test))]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Owner {
    Own,
    // Another user's, or gone since the list was taken.
    Other,
    Unread,
}

// WTS's SID first, then the session's user, so another user's process is never opened.
#[cfg(any(windows, test))]
pub(crate) fn owner(
    own: &str,
    listed: Option<&str>,
    anothers_session: impl FnOnce() -> bool,
    token: impl FnOnce() -> Told,
) -> Owner {
    if let Some(sid) = listed {
        return if sid == own { Owner::Own } else { Owner::Other };
    }
    if anothers_session() {
        return Owner::Other;
    }
    match token() {
        Told::User(sid) if sid == own => Owner::Own,
        Told::User(_) | Told::Gone => Owner::Other,
        Told::Refused => Owner::Unread,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const OWN: &str = "S-1-5-21-1-2-3-1001";
    const OTHER: &str = "S-1-5-21-1-2-3-1002";

    fn unasked() -> bool {
        panic!("the session was asked about")
    }

    fn unopened() -> Told {
        panic!("the process was opened")
    }

    #[test]
    fn a_sid_listed_with_the_process_decides_without_opening_it() {
        assert_eq!(owner(OWN, Some(OWN), unasked, unopened), Owner::Own);
        assert_eq!(owner(OWN, Some(OTHER), unasked, unopened), Owner::Other);
    }

    #[test]
    fn another_users_process_is_ignored_and_never_opened() {
        assert_eq!(owner(OWN, None, || true, unopened), Owner::Other);
        assert_eq!(
            owner(OWN, None, || false, || Told::User(OTHER.into())),
            Owner::Other
        );
    }

    // Such as the same person's elevated codex.
    #[test]
    fn a_process_whose_owner_cannot_be_read_is_unread() {
        assert_eq!(owner(OWN, None, || false, || Told::Refused), Owner::Unread);
        assert_eq!(
            owner(OWN, None, || false, || Told::User(OWN.into())),
            Owner::Own
        );
        assert_eq!(owner(OWN, None, || false, || Told::Gone), Owner::Other);
    }
}
