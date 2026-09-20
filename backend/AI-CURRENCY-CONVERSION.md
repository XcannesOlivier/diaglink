# Supplier cost conversion to EUR

AiCostCalculator continues to return the supplier currency. AiCostCurrencyConverter
is a separate, read-only service: ConvertAsync(amount, currency, usage, cancellationToken)
uses usage.CreatedAtUtc. An overload accepts that UTC timestamp explicitly.
No current-time lookup, external API, rate seeding, inverse rate or triangulation is used.

For BaseCurrency USD, QuoteCurrency EUR and Rate X: EUR = USD * X.
Rates use decimal(18,10), with validity [EffectiveFromUtc, EffectiveToUtc).
The exact currency pair and consumption timestamp must match one row; zero or multiple
matches fail explicitly. Currency syntax is three uppercase ASCII letters, not an ISO registry lookup.
SQL datetime2 values with Unspecified Kind represent UTC; Local dates are rejected.

Multiplication uses decimal and the converted sum is rounded once to six decimal places,
AwayFromZero, with overflow and decimal(18,6) range checks. EUR to EUR returns the original
supplier amount unchanged and rate 1, without a database rate or fabricated rate ID.
Inputs normally come from AiCostCalculator and are already six-decimal amounts.

The result retains source amount/currency, target EUR, selected rate, row ID and effective start.
For reproducibility, historical rows must be retained and their rate, currency pair and start
must not be edited retroactively. Introduce new periods and close the previous period at the
boundary. This stage provides no rate editing endpoint or immutable audit enforcement.
Overlaps are rejected by the converter rather than resolved by ordering.

AddExchangeRates only creates dbo.ExchangeRates, its primary key, positive-rate and valid-interval
checks, and the nonunique (BaseCurrency, QuoteCurrency, EffectiveFromUtc) index.
It contains no seed and must be reviewed before any future application to Azure SQL.
No conversion is persisted and no billing or credit processing is connected.
