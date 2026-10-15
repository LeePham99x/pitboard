// Whose a process is on Windows, decided on every system from what Windows said of it.

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Told {
    User(String),
    Refused,
    Gone,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Owner {
    Own,
    // Another user's, or gone since the list was taken.
    Other,
    Unread,
}

// What WTS said of a Toolhelp process: it did not answer at all, it answered but did not list
// the process, or it listed it with its session and its owner's SID where it gave one.
pub(crate) enum Listing<'a> {
    Unanswered,
    Unlisted,
    Listed { session: u32, sid: Option<&'a str> },
}

// WTS first: a SID it gave decides, and a process it answered for but did not list is another
// user's (WTSEnumerateProcessesExW shows another user's processes to administrators only) or
// gone since the snapshot, so it is Other and never opened. The token is read only where WTS
// did not answer, or listed the process without a SID, and then another user's session is
// passed over unopened too.
pub(crate) fn owner(
    own: &str,
    listing: Listing,
    anothers_session: impl FnOnce(u32) -> bool,
    token: impl FnOnce() -> Told,
) -> Owner {
    let session = match listing {
        Listing::Listed { sid: Some(sid), .. } => {
            return if sid == own { Owner::Own } else { Owner::Other };
        }
        Listing::Listed { session, sid: None } => Some(session),
        Listing::Unlisted => return Owner::Other,
        Listing::Unanswered => None,
    };
    if let Some(session) = session
        && anothers_session(session)
    {
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

    fn unasked(_: u32) -> bool {
        panic!("the session was asked about")
    }

    fn unopened() -> Told {
        panic!("the process was opened")
    }

    #[test]
    fn a_sid_listed_with_the_process_decides_without_opening_it() {
        let own = Listing::Listed {
            session: 1,
            sid: Some(OWN),
        };
        let other = Listing::Listed {
            session: 1,
            sid: Some(OTHER),
        };
        assert_eq!(owner(OWN, own, unasked, unopened), Owner::Own);
        assert_eq!(owner(OWN, other, unasked, unopened), Owner::Other);
    }

    // WTS answered but did not list it: another user's session, or gone since the snapshot.
    #[test]
    fn an_unlisted_process_with_a_wts_answer_is_other_and_never_opened() {
        assert_eq!(
            owner(OWN, Listing::Unlisted, unasked, unopened),
            Owner::Other
        );
    }

    #[test]
    fn a_listed_process_in_another_users_session_is_ignored_and_never_opened() {
        let no_sid = || Listing::Listed {
            session: 7,
            sid: None,
        };
        assert_eq!(owner(OWN, no_sid(), |_| true, unopened), Owner::Other);
        assert_eq!(
            owner(OWN, no_sid(), |_| false, || Told::User(OTHER.into())),
            Owner::Other
        );
    }

    // Such as the same person's elevated codex, which WTS lists without a SID.
    #[test]
    fn a_process_whose_owner_cannot_be_read_is_unread() {
        let no_listing = || Listing::Unanswered;
        assert_eq!(
            owner(OWN, no_listing(), unasked, || Told::Refused),
            Owner::Unread
        );
        assert_eq!(
            owner(OWN, no_listing(), |_| false, || Told::User(OWN.into())),
            Owner::Own
        );
        assert_eq!(
            owner(OWN, no_listing(), |_| false, || Told::Gone),
            Owner::Other
        );
    }
}
