//! What Pitboard does by itself, for a front end somebody asked to: the app with its setting
//! on, or `pitboard watch` running in a terminal.
//!
//! Each thing it does is a duty: a rule that judges from what Pitboard already knows, a look
//! at its files that waits on nobody, and an act under the lock every change takes. The
//! automatic switch ([`crate::autoswitch`]) is one. What every duty shares is here: how often
//! a front end decides, how long something that came to nothing waits before it is tried
//! again, and how what was tried is kept between runs. What each duty decides, and what it
//! does, is its own.

pub mod cadence;
pub(crate) mod ledger;
