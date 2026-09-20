import React, { useCallback } from 'react';
import { TabList, Tab, makeStyles } from '@fluentui/react-components';
import type { SelectTabData, SelectTabEvent } from '@fluentui/react-components';
import { getNavItemsForRole } from '../../utils/navigation';
import type { AppView } from '../../types/navigation';
import type { DiagLinkRole } from '../../types/currentUser';

const useStyles = makeStyles({
  root: {
    minWidth: 0,
  },
});

interface NavigationProps {
  role: DiagLinkRole | undefined;
  currentView: AppView;
  onSelectView: (view: AppView) => void;
}

/** Role-gated primary navigation — UX only, the backend is the actual authority on access. */
export const Navigation: React.FC<NavigationProps> = ({ role, currentView, onSelectView }) => {
  const styles = useStyles();
  const items = getNavItemsForRole(role);

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
        <Tab key={item.view} value={item.view}>
          {item.label}
        </Tab>
      ))}
    </TabList>
  );
};
