# LedgerPay

LedgerPay is a wallet service that moves Turkish lira between accounts and writes every movement into a double-entry ledger, so money is never created or lost.

## Language

### Accounts

**Account**:
A place where money is held for one owner.
_Avoid_: Wallet, pocket

**Owner**:
The person or business an account belongs to.
_Avoid_: User, customer

**Customer account**:
Any account that belongs to an owner. Its balance can never go below zero.
_Avoid_: User account, personal account

**External funding account**:
The one system account that stands for money outside LedgerPay. Deposits come from it, and it is the only account whose balance may go below zero.
_Avoid_: Bank account, cash account, world account

**Balance**:
How much money an account holds right now: the sum of all of its ledger entries.
_Avoid_: Funds, total

### Ledger

**Ledger**:
The complete, append-only record of every money movement in LedgerPay.
_Avoid_: Journal, history, log

**Ledger entry**:
One line of the ledger: a signed amount that one transfer added to or took from one account.
_Avoid_: Posting, record, transaction line

**Transfer**:
One movement of an amount from one account to another. It always writes exactly two ledger entries, and they sum to zero.
_Avoid_: Transaction, payment

**Deposit**:
A transfer from the external funding account into a customer account.
_Avoid_: Top-up, load

**Amount**:
A positive sum of Turkish lira, counted in kuruş (one hundredth of a lira).
_Avoid_: Value, sum, money

**Idempotency key**:
A text the client chooses and sends with every deposit and transfer. A request sent again with the same key moves no more money and gets back the transfer the first request created.
_Avoid_: Request id, dedup key

**Risk check**:
The question LedgerPay asks the risk service before every deposit and transfer: may this money move? If the risk check says no, or gives no answer in time, no money moves.
_Avoid_: Fraud check, validation

**Reconciliation run**:
One check that the ledger still holds together: all ledger entries add up to zero, every transfer of the checked day has exactly two entries that add up to zero, and no customer account is below zero. Its result is Passed or Failed. One runs every night for the day before; more can be started by hand at any time.
_Avoid_: Audit, consistency check, health check

### Events

**Transfer completed**:
The event that says a transfer has been written to the ledger. Other services learn about every transfer, deposits included, from this event.
_Avoid_: Transfer created, payment sent

**Notification**:
A short message that tells an owner that money arrived in one of their accounts. Each transfer completed event gives at most one notification.
_Avoid_: Alert, push, message
