import {GlobalFinancePeriod} from './GlobalFinancePeriod';
import React, { useCallback, useId, useState } from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { ChevronDown20Regular, ChevronRight20Regular } from '@fluentui/react-icons';
import { PlaceholderView } from './PlaceholderView';
import { StripeAdminPanel } from './StripeAdminPanel';
import type { AdminSection } from './adminAccordion';
import { useAuth } from '../../hooks/useAuth';

const useStyles = makeStyles({
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
    gap: tokens.spacingHorizontalM,
  },
  futureCard: {
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground3,
  },
  statistics: {
    gridColumn: '1 / -1',
    minWidth: 0,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  heading: { margin: 0 },
  header: {
    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM, width: '100%', minWidth: 0,
    padding: 0, border: 0, backgroundColor: 'transparent',
    color: 'inherit', fontFamily: 'inherit', textAlign: 'left', cursor: 'pointer',
    '&:focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '4px' },
  },
  headerText: { minWidth: 0, overflowWrap: 'anywhere' },
  chevron: { flexShrink: 0 },
  metrics: {
    marginTop: tokens.spacingVerticalL,
    display: 'grid',
    gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
    gap: tokens.spacingHorizontalM,
    '@media (max-width: 1000px)': {
      gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    },
    '@media (max-width: 600px)': {
      gridTemplateColumns: 'minmax(0, 1fr)',
    },
  },
  metric: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  muted: { color: tokens.colorNeutralForeground3 },
});

const FUTURE_SECTIONS = [
  'Statistiques globales',
];

/** diaglink_super_admin only — future home for global tooling. */
export const DiagLinkAdminView: React.FC<{ onDiagLinkSessionExpired?: () => void }> = ({ onDiagLinkSessionExpired }) => {
  const styles = useStyles();
  const { getAccessToken } = useAuth();
  const [openSection, setOpenSection] = useState<AdminSection | null>(null);
  const statisticsContentId = useId();
  const onSectionToggle = useCallback((section: AdminSection, isOpen: boolean) => {
    setOpenSection(current => isOpen ? section : current === section ? null : current);
  }, []);
  const statisticsOpen = openSection === 'statistics';

  return (
    <PlaceholderView title="Administration DiagLink" subtitle="Espace réservé au super administrateur DiagLink.">
      <div className={styles.grid}>
        {FUTURE_SECTIONS.map(section => (
          section === 'Statistiques globales' ? (
            <section key={section} className={styles.statistics} aria-label="Statistiques globales">
              <h2 className={styles.heading}>
                <button type="button" className={styles.header}
                  aria-expanded={statisticsOpen} aria-controls={statisticsContentId}
                  onClick={() => onSectionToggle('statistics', !statisticsOpen)}>
                  <span className={styles.headerText}>
                    <Text size={500} weight="semibold">Statistiques globales</Text>
                  </span>
                  {statisticsOpen
                    ? <ChevronDown20Regular className={styles.chevron} aria-hidden="true" />
                    : <ChevronRight20Regular className={styles.chevron} aria-hidden="true" />}
                </button>
              </h2>
              <div id={statisticsContentId} hidden={!statisticsOpen}>
              <GlobalFinancePeriod token={getAccessToken}/>
              </div>
            </section>
          ) : null
        ))}
        <StripeAdminPanel getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired}
          accordion={{ openSection, onSectionToggle }} />
      </div>
    </PlaceholderView>
  );
};
