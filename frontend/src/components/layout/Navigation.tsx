import React, { useCallback } from 'react';
import { Badge, TabList, Tab, makeStyles } from '@fluentui/react-components';
import type { SelectTabData, SelectTabEvent } from '@fluentui/react-components';
import { getNavItemsForRole } from '../../utils/navigation';
import type { AppView } from '../../types/navigation';
import type { DiagLinkRole } from '../../types/currentUser';

const useStyles = makeStyles({
  root: {
    minWidth: 0,
  },
  tabContent: { display: 'inline-flex', alignItems: 'center', gap: '6px' },
});

interface NavigationProps {
  role: DiagLinkRole | undefined;
  currentView: AppView;
  onSelectView: (view: AppView) => void;
  pendingMachineRequestCount?: number;
}

/** Role-gated primary navigation — UX only, the backend is the actual authority on access. */
export const Navigation: React.FC<NavigationProps> = ({ role, currentView, onSelectView, pendingMachineRequestCount = 0 }) => {
  const styles = useStyles();
  const items = getNavItemsForRole(role, pendingMachineRequestCount);

  const handleSelect = useCallback((_: SelectTabEvent, data: SelectTabData) => {
    onSelectView(data.value as AppView);
  }, [onSelectView]);

  // 'history' highlights alongside 'chat' since it renders the same chat surface with the sidebar open.
  const selectedValue = currentView === 'history' ? 'history' : currentView;

  return (
    <TabList
      className={styles.root}
      selectedValue={selectedValue}
      onTabSelect={handleSelect}
      size="medium"
    >
      {items.map(item => (
        <Tab key={item.view} value={item.view} aria-label={item.label} title={item.label}>
          <span className={styles.tabContent}>
            <span>{item.label}</span>
            {item.badgeCount !== undefined && <Badge appearance="filled" color="important" size="small">{item.badgeCount}</Badge>}
          </span>
        </Tab>
      ))}
    </TabList>
  );
};
