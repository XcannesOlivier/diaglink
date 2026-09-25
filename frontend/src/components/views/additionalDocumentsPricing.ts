export const additionalDocumentsPricePerPageCents = 27;
export const additionalDocumentsMinimumAmountCents = 50;

export const calculateAdditionalDocumentsAmountCents = (totalPages: number) =>
  totalPages <= 0
    ? 0
    : Math.max(totalPages * additionalDocumentsPricePerPageCents, additionalDocumentsMinimumAmountCents);
