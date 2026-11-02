# An external funding account balances deposits

Money that enters LedgerPay comes from one system account, the external funding account, and a deposit is an ordinary transfer from it. It is the only account allowed to go below zero, and its negative balance is the total money that has come in from outside. This keeps the sum of the whole ledger at zero at all times, so a single query can prove that no money was created or lost, and deposits need no rules of their own.
