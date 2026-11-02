# Derive balances from ledger entries

Accounts have no balance column. An account's balance is the sum of its ledger entries, and entries are never updated or deleted; a mistake is fixed by a new transfer in the opposite direction. A stored balance could drift away from the entries with no way to tell which one is right, while with the entries as the only source every kuruş can be traced to the transfer that moved it. The cost is that reading a balance sums rows, which gets slower as an account's history grows.
