// Copyright (c) OPC Foundation. Licensed under the MIT License.
//! Bounded send leases. Acceptance is not packetization; reset final size is separate evidence.

use crate::config::{Limits, MAX_VARINT};
use crate::error::{Code, Failure};
use std::collections::{BTreeMap, VecDeque};

/// Hard bound on queued plus caller-leased output payload bytes.
pub const MAX_OUTPUT_BYTES: usize = 1024 * 1024;
/// Hard bound on live send descriptors, independently of their byte lengths.
pub const MAX_PENDING_SENDS: usize = 512;

#[derive(Debug)]
struct Pending {
    stream: u64,
    offset: u64,
    len: usize,
    accepted: usize,
    leased: bool,
    datagram: bool,
    prefix: bool,
    fin: bool,
}

#[derive(Clone, Copy, Debug)]
struct Receipt {
    token: u64,
    accepted: usize,
    flags: u64,
}

#[derive(Clone, Copy, Debug, Default)]
struct Progress {
    next: u64,
    accepted: u64,
    completed: u64,
    leased_end: u64,
    fin_completed: bool,
}

#[derive(Clone, Copy, Debug)]
pub struct Completion {
    pub stream: u64,
    pub partial_terminal: bool,
    pub prefix_failed: bool,
}

#[derive(Debug)]
pub struct SendBook {
    next_token: u64,
    pending: BTreeMap<u64, Pending>,
    progress: BTreeMap<u64, Progress>,
    recent: VecDeque<Receipt>,
    limits: Limits,
}

impl SendBook {
    pub fn new(limits: Limits) -> Self {
        Self {
            next_token: 0,
            pending: BTreeMap::new(),
            progress: BTreeMap::new(),
            recent: VecDeque::with_capacity(limits.receipts as usize),
            limits,
        }
    }

    pub fn register(
        &mut self,
        stream: u64,
        len: usize,
        datagram: bool,
        prefix: bool,
        fin: bool,
    ) -> Result<(u64, u64), Failure> {
        if self.pending.len() >= self.limits.pending_sends as usize {
            return Err(Failure::local(
                Code::Backpressure,
                "send descriptor admission limit",
            ));
        }
        let token = self
            .next_token
            .checked_add(1)
            .ok_or_else(|| Failure::local(Code::LimitExceeded, "send token exhausted"))?;
        let offset = if datagram {
            0
        } else {
            if !self.progress.contains_key(&stream)
                && self.progress.len() >= self.limits.lifetime_streams as usize
            {
                return Err(Failure::local(Code::LimitExceeded, "send stream lifetime limit"));
            }
            let next = self
                .progress
                .get(&stream)
                .map_or(0, |value| value.next)
                .checked_add(len as u64)
                .filter(|value| *value <= MAX_VARINT)
                .ok_or_else(|| Failure::local(Code::LimitExceeded, "send offset exhausted"))?;
            let progress = self.progress.entry(stream).or_default();
            let offset = progress.next;
            progress.next = next;
            offset
        };
        self.next_token = token;
        self.pending.insert(
            token,
            Pending {
                stream,
                offset,
                len,
                accepted: 0,
                leased: false,
                datagram,
                prefix,
                fin,
            },
        );
        Ok((token, offset))
    }

    pub fn can_lease(&self, token: u64) -> bool {
        let Some(send) = self.pending.get(&token) else {
            return false;
        };
        if send.leased {
            return false;
        }
        send.datagram
            || self
                .pending
                .range(..token)
                .all(|(_, earlier)| earlier.datagram || earlier.stream != send.stream)
    }

    pub fn lease(&mut self, token: u64) {
        if let Some(send) = self.pending.get_mut(&token) {
            send.leased = true;
            if !send.datagram {
                let p = self.progress.get_mut(&send.stream).expect("registered stream");
                p.leased_end = p.leased_end.max(send.offset + send.len as u64);
            }
        }
    }

    pub fn leased_bytes(&self) -> usize {
        self.pending
            .values()
            .filter(|send| send.leased)
            .map(|send| send.len)
            .sum()
    }

    pub fn pending_count(&self) -> usize {
        self.pending.len()
    }

    pub fn has_leases(&self, stream: u64) -> bool {
        self.pending
            .values()
            .any(|send| send.stream == stream && !send.datagram && send.leased)
    }

    pub fn accepted(&self, stream: u64) -> u64 {
        self.progress.get(&stream).map_or(0, |p| p.accepted)
    }

    pub fn leased_end(&self, stream: u64) -> u64 {
        self.progress.get(&stream).map_or(0, |p| p.leased_end)
    }

    pub fn completed(&self, stream: u64) -> u64 {
        self.progress.get(&stream).map_or(0, |p| p.completed)
    }

    pub fn fin_completed(&self, stream: u64) -> bool {
        self.progress.get(&stream).is_some_and(|p| p.fin_completed)
    }

    /// Cumulative acknowledgement; done means native code no longer uses the caller's buffer.
    pub fn acknowledge(
        &mut self,
        token: u64,
        accepted: usize,
        flags: u64,
    ) -> Result<Option<Completion>, Failure> {
        let done = flags & 1 != 0;
        let fin = flags & 2 != 0;
        let failed = flags & 4 != 0;
        if flags > 7 || ((fin || failed) && !done) || (fin && failed) {
            return Err(Failure::local(
                Code::InvalidCompletion,
                "invalid completion flags",
            ));
        }
        if let Some(receipt) = self.recent.iter().find(|r| r.token == token) {
            return if done && receipt.accepted == accepted && receipt.flags == flags {
                Ok(None)
            } else {
                Err(Failure::local(
                    Code::InvalidCompletion,
                    "completion replay differs from recorded result",
                ))
            };
        }
        let send = self
            .pending
            .get(&token)
            .ok_or_else(|| Failure::local(Code::InvalidCompletion, "unknown or retired send token"))?;
        if !send.leased {
            return Err(Failure::local(
                Code::InvalidCompletion,
                "send token has not been leased",
            ));
        }
        if accepted < send.accepted || accepted > send.len {
            return Err(Failure::local(
                Code::InvalidCompletion,
                "send acceptance must be monotonic and within the lease",
            ));
        }
        if send.datagram && accepted != 0 && accepted != send.len {
            return Err(Failure::local(
                Code::InvalidCompletion,
                "a datagram cannot be partially accepted",
            ));
        }
        if fin && (!send.fin || accepted != send.len) {
            return Err(Failure::local(
                Code::InvalidCompletion,
                "FIN acknowledgement requires a fully accepted FIN write",
            ));
        }
        let completion = Completion {
            stream: send.stream,
            partial_terminal: done
                && (failed || accepted != send.len || (send.fin && !fin))
                && !send.datagram,
            prefix_failed: done && (failed || accepted != send.len) && send.prefix,
        };
        let send = self.pending.get_mut(&token).expect("validated send");
        send.accepted = accepted;
        if !send.datagram {
            let progress = self.progress.get_mut(&send.stream).expect("registered progress");
            progress.accepted = progress.accepted.max(send.offset + accepted as u64);
            if done && accepted == send.len && !failed {
                progress.completed = send.offset + accepted as u64;
                progress.fin_completed |= fin;
            }
        }
        if done {
            self.pending.remove(&token);
            if self.recent.len() == self.limits.receipts as usize {
                self.recent.pop_front();
            }
            self.recent.push_back(Receipt {
                token,
                accepted,
                flags,
            });
        }
        Ok(Some(completion))
    }

    /// Cancel only buffers still owned by the adapter, never a caller-leased buffer or prefix.
    pub fn cancel_unleased(&mut self, stream: u64) -> Vec<u64> {
        let tokens: Vec<_> = self
            .pending
            .iter()
            .filter_map(|(&token, send)| {
                (!send.datagram && send.stream == stream && !send.leased && !send.prefix).then_some(token)
            })
            .collect();
        for token in &tokens {
            self.pending.remove(token);
        }
        tokens
    }

    /// Drop only adapter-owned datagrams when the sole session closes.
    pub fn cancel_unleased_datagrams(&mut self) -> Vec<u64> {
        let tokens: Vec<_> = self
            .pending
            .iter()
            .filter_map(|(&token, send)| (send.datagram && !send.leased).then_some(token))
            .collect();
        for token in &tokens {
            self.pending.remove(token);
        }
        tokens
    }
}
