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
