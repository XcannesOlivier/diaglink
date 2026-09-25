export const INCLUDED_PAGE_COUNT = 400;
export const PREPARATION_BASE_CENTS = 9_990;
export const EXTRA_PAGE_CENTS = 27;
export const MAXIMUM_FIRST_SUBSCRIPTION_CENTS = 2_990;

export function calculatePreparationPrice(totalPages: number) {
  const extraPages = Math.max(0, totalPages - INCLUDED_PAGE_COUNT);

  return {
    extraPages,
    extraPriceCents: extraPages * EXTRA_PAGE_CENTS,
    totalPriceCents: PREPARATION_BASE_CENTS + extraPages * EXTRA_PAGE_CENTS,
  };
}

export function calculateMaximumAuthorizationPrice(totalPages: number) {
  return calculatePreparationPrice(totalPages).totalPriceCents + MAXIMUM_FIRST_SUBSCRIPTION_CENTS;
}
