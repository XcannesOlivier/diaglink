import { describe, expect, it } from 'vitest';
import { trimStartFormValues, validateStartForm, type StartFormValues } from './startFormValidation';

const validValues: StartFormValues = {
  firstName: 'Jean',
  lastName: 'Martin',
  company: 'Atelier Industrie',
  email: 'jean.martin@example.com',
  phone: '+33 6 12 34 56 78',
  machineName: 'Compresseur 1',
  manufacturer: 'Fabricant',
  model: 'GA90',
  serialNumber: '',
  description: '',
};

describe('first-machine form validation', () => {
  it('accepts a complete form with one valid PDF', () => {
    expect(validateStartForm(validValues, 1)).toEqual({});
  });

  it('rejects required values containing only spaces and missing documents', () => {
    const errors = validateStartForm({ ...validValues, firstName: '   ', machineName: '\t' }, 0);
    expect(errors.firstName).toBeDefined();
    expect(errors.machineName).toBeDefined();
    expect(errors.documents).toBeDefined();
  });

  it('validates email and international-friendly phone formats', () => {
    expect(validateStartForm({ ...validValues, email: 'incorrect', phone: 'abc' }, 1)).toMatchObject({
      email: 'Saisissez une adresse e-mail valide.',
      phone: 'Saisissez un numéro de téléphone valide.',
    });
    expect(validateStartForm({ ...validValues, phone: '(212) 555-0198' }, 1)).toEqual({});
  });

  it('trims required and optional text values', () => {
    expect(trimStartFormValues({ ...validValues, firstName: '  Jean  ', description: '  Note  ' })).toMatchObject({
      firstName: 'Jean',
      description: 'Note',
    });
  });
});
