import type { DetailsHTMLAttributes, SyntheticEvent } from 'react';

export type AdminSection = 'statistics' | 'consumption' | 'payments' | 'billing' | 'technical' | 'repairs';

export interface AdminAccordionControl {
  openSection: AdminSection | null;
  onSectionToggle: (section: AdminSection, isOpen: boolean) => void;
}

export function getAdminAccordionProps(
  control: AdminAccordionControl | undefined,
  section: AdminSection,
): Pick<DetailsHTMLAttributes<HTMLDetailsElement>, 'open' | 'onToggle'> {
  if (!control) return {};

  return {
    open: control.openSection === section,
    onToggle: (event: SyntheticEvent<HTMLDetailsElement>) => {
      if (event.target !== event.currentTarget) return;
      control.onSectionToggle(section, event.currentTarget.open);
    },
  };
}