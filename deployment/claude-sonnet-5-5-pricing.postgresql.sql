BEGIN;

LOCK TABLE "dbo"."AiPricing" IN SHARE ROW EXCLUSIVE MODE;

DO $pricing$
DECLARE
    now_utc timestamp with time zone := CURRENT_TIMESTAMP;
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM "dbo"."AiPricing"
        WHERE "Provider" = 'Anthropic'
          AND "Model" = 'claude-sonnet-5-5'
          AND "UsageType" IS NULL
          AND "EffectiveFromUtc" <= now_utc
          AND ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > now_utc)
          AND
          (
              "InputPricePerMillion" IS DISTINCT FROM 2.00000000
              OR "OutputPricePerMillion" IS DISTINCT FROM 10.00000000
              OR "CacheReadPricePerMillion" IS DISTINCT FROM 0.10000000
              OR "CacheCreation5mPricePerMillion" IS DISTINCT FROM 2.50000000
              OR "CacheCreation1hPricePerMillion" IS DISTINCT FROM 4.00000000
              OR "WebSearchPricePerRequest" IS DISTINCT FROM 0.01000000
              OR "Currency" IS DISTINCT FROM 'USD'
          )
    ) THEN
        RAISE EXCEPTION 'An active claude-sonnet-5-5 pricing row exists with different values.';
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM "dbo"."AiPricing"
        WHERE "Provider" = 'Anthropic'
          AND "Model" = 'claude-sonnet-5-5'
          AND "UsageType" IS NULL
          AND "EffectiveFromUtc" <= now_utc
          AND ("EffectiveToUtc" IS NULL OR "EffectiveToUtc" > now_utc)
    ) THEN
        INSERT INTO "dbo"."AiPricing"
        (
            "Id",
            "Provider",
            "Model",
            "UsageType",
            "InputPricePerMillion",
            "OutputPricePerMillion",
            "CacheReadPricePerMillion",
            "CacheCreation5mPricePerMillion",
            "CacheCreation1hPricePerMillion",
            "WebSearchPricePerRequest",
            "Currency",
            "EffectiveFromUtc",
            "EffectiveToUtc",
            "CreatedAtUtc"
        )
        VALUES
        (
            gen_random_uuid(),
            'Anthropic',
            'claude-sonnet-5-5',
            NULL,
            2.00000000,
            10.00000000,
            0.10000000,
            2.50000000,
            4.00000000,
            0.01000000,
            'USD',
            now_utc,
            NULL,
            now_utc
        );
    END IF;
END
$pricing$;

COMMIT;

SELECT
    "Provider",
    "Model",
    "UsageType",
    "InputPricePerMillion",
    "OutputPricePerMillion",
    "CacheReadPricePerMillion",
    "CacheCreation5mPricePerMillion",
    "CacheCreation1hPricePerMillion",
    "WebSearchPricePerRequest",
    "Currency",
    "EffectiveFromUtc",
    "EffectiveToUtc"
FROM "dbo"."AiPricing"
WHERE "Provider" = 'Anthropic'
  AND "Model" IN ('claude-sonnet-5', 'claude-sonnet-5-5')
ORDER BY "Model", "EffectiveFromUtc";
