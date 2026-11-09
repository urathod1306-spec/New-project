# DD-08 review — consolidated report

Three fresh reviewers read DD-08 v1 and the code read-only: **G1 Comprehension** (23 ambiguities), **G2 Critic** (28 findings: 11 Must, 13 Should, 4 Nitpick) and **G3 Implementation readiness** (21 items, 3 blocking). The owner answered two further questions (D15 MSME rule, D16 no files on converted e-Notes). Every finding is resolved in **v2**.

## G1
| ID | Resolution |
|---|---|
| G1-1 | FY from inward date (office time) — §2 |
| G1-2 | GEN shared series documented (D6); codes unique per list (E1) |
| G1-3 | Office resolved by id; name match case-insensitive (§6.3); CleanCode path fixed |
| G1-4 | Invoices never reopened by reminders; reply-due kept on reopen (E4, §3.2) |
| G1-5 | Reminder held by inwarder, closure `Reminder` (E4) |
| G1-6 | Inwarder cannot certify (E10) |
| G1-7 | Combined invoice table lists operator actions (§3.3) |
| G1-8 | Flags from receipt, against MSME due (D15) |
| G1-9 | Owner chose simple 45-day rule (D15) |
| G1-10 | Payments compared exactly (§3.4) |
| G1-11 | Reversal rules and REV- voucher (§3.3, §4) |
| G1-12 | Allowed states and amount signs (§3.3, §3.4, §4 CHECKs) |
| G1-13 | Warnings listed (§3.5) |
| G1-14 | Amount = net; ambiguous → unmatched; idempotency by (item, voucher) (§6.11) |
| G1-15 | Confidential index: number + invoice no only (E8) |
| G1-16 | Full access = P/IV/IA; precedence stated (§5) |
| G1-17 | Self-inward limited to own office (§6.3) |
| G1-18 | Draft created first; idempotent retry via SourceInwardId (E5) |
| G1-19 | Office time; working days Mon–Fri, digest only (§8) |
| G1-20 | Category insert WHERE NOT EXISTS, the only data insert (§4) |
| G1-21 | Accept applies to invoices; counted in InwardMine (§3.3, §8) |
| G1-22 | Tile status sets defined (§6.7) |
| G1-23 | Owner answered D15/D16; E1–E10 for confirmation |

## G2
| ID | Class | Resolution |
|---|---|---|
| G2-1 | OWNER | Owner chose simple rule D15; statutory model recorded as alternative |
| G2-2 | PROPOSE FIX | Edit details + Cancel (entered in error) (§3.2, §3.3) |
| G2-3 | PROPOSE FIX | UNIQUE(ItemId, VoucherRef); REV- vouchers |
| G2-4 | PROPOSE FIX | No files on converted notes (D16), confidential inherited (E5) |
| G2-5 | PROPOSE FIX | Transfer page, inactive-holder tile, IV/OP re-mark, Access warnings |
| G2-6 | PROPOSE FIX | InvoiceAdjustments (credit/debit notes) |
| G2-7 | PROPOSE FIX | Enum CHECKs dropped; validated in code (E2) |
| G2-8 | PROPOSE FIX | Segregation E10 |
| G2-9 | PROPOSE FIX | Visible originals only; invoices not reopened (E4) |
| G2-10 | PROPOSE FIX | Combined transition table; invoices have no reply-due (§3.3) |
| G2-11 | PROPOSE FIX | Confidential: index number/invoice no only; vendor masked in aggregates (E8) |
| G2-12 | PROPOSE FIX | Inward date in office time; back-dating ≤ 90 days |
| G2-13 | PROPOSE FIX | OfficeId NOT NULL DEFAULT 0 + CHECK; no self-grant; multi-valued cache |
| G2-14 | PROPOSE FIX | Extra warnings; checked confirmation on first payment; ambiguous ERP → unmatched |
| G2-15 | PROPOSE FIX | 43B(h) as-on any date, all outstanding, traders excluded, class-not-recorded section, entered-by cut-off; s.16 interest deferred (§11) |
| G2-16 | PROPOSE FIX | Reversal from any state with payments; CHECKs on signs/source |
| G2-17 | PROPOSE FIX | Close short notified to IVs + tile |
| G2-18 | PROPOSE FIX | Office by id; GEN reserved; own office for self-inward |
| G2-19 | PROPOSE FIX | Returned-to-registry users lose access; confidential copy confirmed |
| G2-20 | PROPOSE FIX | Participants table; index-friendly SQL; PDF cap 31 days/5,000 rows; benchmark with 3-office operator |
| G2-21 | PROPOSE FIX | Payments in IMMEDIATE tx from SUM; RowVersion on invoice actions |
| G2-22 | PROPOSE FIX | Other charges field; ±₹1 |
| G2-23 | PROPOSE FIX | Override reason not in audit |
| G2-24 | PROPOSE FIX | Code NOCASE; owner by unique sign-in name |
| G2-25 | CONTEST | Same pattern as ENoteSearch (migration 5, same SQLite) — no change |
| G2-26 | PROPOSE FIX | Amount = net; tolerant parsing; XLSX inline strings only |
| G2-27 | PROPOSE FIX | No PAN column; GSTIN shown only to full-access users |
| G2-28 | PROPOSE FIX | 5-digit minimum serial; IRN optional; same sender-ref warning |

## G3
| ID | Resolution |
|---|---|
| G3-1 | Combined table (§3.3) |
| G3-2 | Edit details (§3.2/3.3) |
| G3-3 | Owner decision D15 |
| G3-4 | CHECK + NOT NULL DEFAULT 0 |
| G3-5 | CertifiedByUserId |
| G3-6 | Index (InwardedByUserId, Id) |
| G3-7 | Codes fixed (§2) |
| G3-8 | E8 |
| G3-9 | E5 + D16; step order (§12 #3 before #6) |
| G3-10 | E4 |
| G3-11 | IV/OP re-mark; transfer; warnings |
| G3-12 | §6.11 |
| G3-13 | §3.4 |
| G3-14 | §2, §6.3 |
| G3-15 | Settings with defaults (§9) |
| G3-16 | §8 table |
| G3-17 | InwardAssigned; RoleKey sorted grants |
| G3-18 | §12 updated |
| G3-19 | MaxImportKB clamped |
| G3-20 | DEFAULT NULL |
| G3-21 | Full page, short content |

## Round 2 — G3 readiness on v2

Verdict: buildable with three blocking items (R2-1 credit/debit note items, R2-2 ActionRequired values, R2-3 voucher uniqueness). All 22 items were accepted as PROPOSE FIX and are recorded as binding in DD-08 §15. R2-3 and R2-8 changed migration 8 before release (no table UNIQUE on vouchers; unique index on `ENotes.SourceInwardId`).

## Post-build hostile review (Prompt R) on the 1.7.0 build

19 findings (no Blocker; 3 High, 10 Medium, 6 Low). All fixed before release; regression tests are the `Review_*` tests in `tests/NabconsIntranet.Tests/InwardTests.cs`.

| # | Sev. | Finding | Resolution |
|---|---|---|---|
| 1 | High | Edit form showed/let a masked operator change a confidential invoice's vendor fields | Invoice part hidden from masked editors and never updated by them |
| 2 | High | Operator could mark or copy a confidential item to themselves and gain full access | Refused unless they already have full access |
| 3 | High | Amount edit after a credit note could leave nothing (or less) to pay | Refused when total + adjustments ≤ 0 or below the settled amount |
| 4 | Med | ERP row matched by inward number ignored its own invoice no./GSTIN | Must agree, else "Unmatched" |
| 5 | Med | MSME report counted reversed and earlier-year late payments; closed-short balance shown outstanding | Late payments: not reversed, within the as-on FY; closed short = nothing outstanding; current status/class documented |
| 6 | Med | Duplicate / sender-reference warnings named items the user cannot open; MSME hint showed Udyam | Numbers only when visible; Udyam/class only with full access |
| 7 | Med | e-Note draft from an earlier holder silently reused | Reuse only by its initiator; otherwise a clear message |
| 8 | Med | Search capped at 5,000 matches before the visibility filter | Cap removed (benchmarked) |
| 9 | Med | Reminder chain could reach a root the user cannot see | Root must be visible too |
| 10 | Med | Cancel, copy-to and Accounts amount edit allowed while on hold | Refused while on hold |
| 11 | Med | Same credit/debit note recordable twice | Refused for a closed note item and a repeated document number |
| 12 | Med | Sender-reference and other-FY lookups scanned the table | Index on (SenderRef, SenderRefDate) added to migration 8 (unreleased); key prefix as an index range |
| 13 | Med | Items older than 90 days could not be edited in the browser | No minimum date on the edit form |
| 14 | Low | Return-to-registry keep rule wrong in both directions | Based on the movement history: held earlier and passed on without returning |
| 15 | Low | Masked rows showed amounts, vouchers, project; GSTIN shown without full access | Masked invoice panel and list cells; GSTIN only with full access. The MSME report keeps amounts (statutory), vendor masked |
| 16 | Low | Operators could add notes they could not read | Notes shown to them on non-confidential items |
| 17 | Low | Overridden-duplicate invoice could not be edited by users without full access | Stored reason kept when unchanged |
| 18 | Low | Same ERP payment file refused a second time | Allowed; already-recorded vouchers are skipped |
| 19 | Low | Copy-to did not check the row version | Checked and bumped |
