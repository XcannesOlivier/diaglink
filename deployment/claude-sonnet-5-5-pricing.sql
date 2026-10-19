SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @NowUtc datetime2 = SYSUTCDATETIME();

BEGIN TRANSACTION;

IF EXISTS
(
    SELECT 1
    FROM dbo.AiPricing WITH (UPDLOCK, HOLDLOCK)
    WHERE Provider = N'Anthropic'
      AND Model = N'claude-sonnet-5-5'
      AND UsageType IS NULL
      AND EffectiveFromUtc <= @NowUtc
      AND (EffectiveToUtc IS NULL OR EffectiveToUtc > @NowUtc)
      AND
      (
          InputPricePerMillion <> 2.00000000
          OR OutputPricePerMillion <> 10.00000000
          OR CacheReadPricePerMillion <> 0.10000000
          OR CacheCreation5mPricePerMillion <> 2.50000000
          OR CacheCreation1hPricePerMillion <> 4.00000000
          OR WebSearchPricePerRequest <> 0.01000000
          OR Currency <> 'USD'
      )
)
BEGIN
    THROW 50001, 'An active claude-sonnet-5-5 pricing row exists with different values.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.AiPricing WITH (UPDLOCK, HOLDLOCK)
    WHERE Provider = N'Anthropic'
      AND Model = N'claude-sonnet-5-5'
      AND UsageType IS NULL
      AND EffectiveFromUtc <= @NowUtc
      AND (EffectiveToUtc IS NULL OR EffectiveToUtc > @NowUtc)
)
BEGIN
    INSERT dbo.AiPricing
    (
        Id,
        Provider,
        Model,
        UsageType,
        InputPricePerMillion,
        OutputPricePerMillion,
        CacheReadPricePerMillion,
        CacheCreation5mPricePerMillion,
        CacheCreation1hPricePerMillion,
        WebSearchPricePerRequest,
        Currency,
        EffectiveFromUtc,
        EffectiveToUtc,
        CreatedAtUtc
    )
    VALUES
    (
        NEWID(),
        N'Anthropic',
        N'claude-sonnet-5-5',
        NULL,
        2.00000000,
        10.00000000,
        0.10000000,
        2.50000000,
        4.00000000,
        0.01000000,
        'USD',
        @NowUtc,
        NULL,
        @NowUtc
    );
END;

COMMIT TRANSACTION;

SELECT
    Provider,
    Model,
    UsageType,
    InputPricePerMillion,
    OutputPricePerMillion,
    CacheReadPricePerMillion,
    CacheCreation5mPricePerMillion,
    CacheCreation1hPricePerMillion,
    WebSearchPricePerRequest,
    Currency,
    EffectiveFromUtc,
    EffectiveToUtc
FROM dbo.AiPricing
WHERE Provider = N'Anthropic'
  AND Model IN (N'claude-sonnet-5', N'claude-sonnet-5-5')
ORDER BY Model, EffectiveFromUtc;
