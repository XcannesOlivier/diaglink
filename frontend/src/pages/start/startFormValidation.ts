export type StartFormValues = {
  firstName: string;
  lastName: string;
  company: string;
  email: string;
  phone: string;
  machineName: string;
  manufacturer: string;
  model: string;
  serialNumber: string;
  description: string;
};

export type RequiredStartField = 'firstName' | 'lastName' | 'company' | 'email' | 'phone' | 'machineName' | 'manufacturer' | 'model';
export type StartFormErrors = Partial<Record<RequiredStartField | 'documents', string>>;

const requiredFields: Array<{ name: RequiredStartField; label: string }> = [
  { name: 'firstName', label: 'Le prénom' },
  { name: 'lastName', label: 'Le nom' },
  { name: 'company', label: 'L’entreprise' },
  { name: 'email', label: 'L’adresse e-mail' },
  { name: 'phone', label: 'Le téléphone' },
  { name: 'machineName', label: 'Le nom de la machine' },
  { name: 'manufacturer', label: 'Le fabricant ou la marque' },
  { name: 'model', label: 'Le modèle' },
];

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const phonePattern = /^\+?[\d\s().-]{7,25}$/;

export function trimStartFormValues(values: StartFormValues): StartFormValues {
  return Object.fromEntries(
    Object.entries(values).map(([name, value]) => [name, value.trim()]),
  ) as StartFormValues;
}

export function validateStartForm(values: StartFormValues, validPdfCount: number): StartFormErrors {
  const trimmedValues = trimStartFormValues(values);
  const errors: StartFormErrors = {};

  requiredFields.forEach(({ name, label }) => {
    if (!trimmedValues[name]) errors[name] = `${label} est requis.`;
  });

  if (trimmedValues.email && !emailPattern.test(trimmedValues.email)) {
    errors.email = 'Saisissez une adresse e-mail valide.';
  }

  const phoneDigitCount = trimmedValues.phone.replace(/\D/g, '').length;
  if (trimmedValues.phone && (!phonePattern.test(trimmedValues.phone) || phoneDigitCount < 6)) {
    errors.phone = 'Saisissez un numéro de téléphone valide.';
  }

  if (validPdfCount === 0) errors.documents = 'Ajoutez au moins un PDF valide.';

  return errors;
}
