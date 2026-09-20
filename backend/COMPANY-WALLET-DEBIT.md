# Company wallet: all-or-nothing payment

SQL Server retry compatibility and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

DebitAsync accepts the trusted server-side real EUR remainder after MachineIncluded.
Inputs use decimal with at most six decimal places. The commercial amount remains
Math.Round(real remainder * 3m, 6, MidpointRounding.AwayFromZero).

Sufficient balance: debit the entire commercial amount and write exactly one ledger
entry containing the entire real remainder and entire commercial debit. No division
by three or proportional coverage remains.

Insufficient balance: Success=false, Status=InsufficientWalletBalance, no ledger,
no balance or timestamp change. WalletDebitEur=0, WalletLedgerEntryId=null,
WalletBalanceBeforeEur=WalletBalanceAfterEur. CommercialCreditMissing is required
minus available balance. RemainingRealAiCostUncovered is the full real remainder.
RemainingCommercialCreditRequired is the full unpaid amount, distinct from missing
additional funds. FailureReason stays null for this normal business outcome.

After a simulated or future real recharge, the same original input can be retried:
no ledger exists from insufficient attempts. Full payment creates one ledger;
subsequent identical calls return AlreadyProcessed with zero new debit and zero
uncovered amounts. Before/after balances describe the current invocation.
An existing entry with different real or commercial amounts is DataInconsistency.

A dedicated EF context and Serializable transaction retain SQL Server UPDLOCK,
HOLDLOCK on the company wallet through the balance check, update and ledger commit.
Exceptions roll back both writes. Infrastructure failures propagate and may be
retried as whole operations. SQLite tests exercise transactions and competing
writers, not the SQL Server hints themselves.

UX_CreditLedger_CompanyWalletUsage remains a unique filtered index on non-null
AiUsageRecordId for CompanyWallet/AiUsage, independent of MachineIncluded.
The existing migration and snapshot are unchanged; application remains a separate
authorized step. No Azure access, automatic recharge, chat hook or Stripe integration.
