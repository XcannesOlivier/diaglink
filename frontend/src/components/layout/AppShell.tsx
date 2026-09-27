import React, { useCallback, useEffect, useState } from 'react';
import {
  makeStyles,
  tokens,
  Drawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  Text,
  Badge,
} from '@fluentui/react-components';
import { Dismiss24Regular, Mail24Regular, Settings24Regular } from '@fluentui/react-icons';
import { useAppState } from '../../hooks/useAppState';
import { useAuth } from '../../hooks/useAuth';
import { AgentChat } from '../AgentChat';
import { Navigation } from './Navigation';
import { UserBadge } from './UserBadge';
import { MachinesView } from '../views/MachinesView';
import { UsersView } from '../views/UsersView';
import { CompanyView } from '../views/CompanyView';
import { CompaniesView } from '../views/CompaniesView';
import { DiagLinkAdminView } from '../views/DiagLinkAdminView';
import { MachineRequestsView } from '../views/MachineRequestsView';
import { SettingsPanel } from '../core/SettingsPanel';
import { SupportContactDialog } from '../core/SupportContactDialog';
import { getNavItemsForRole, resolveView, ROLE_LABELS } from '../../utils/navigation';
import logoDiagLink from '../../assets/Logo DiagLink.png';
import type { AppView } from '../../types/navigation';
import { listMachineRequests, type MachineRequestDetail, type MachineRequestListItem } from '../../services/machineRequestAdminApi';

const useStyles = makeStyles({
  shell: {
    height: '100%',
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalL,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
    '@media (max-width: 1000px)': {
      display: 'none',
    },
  },
  brand: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
  },
  brandLogo: {
    height: '28px',
    width: 'auto',
    objectFit: 'contain',
    '@media (max-width: 1000px)': {
      height: '22px',
    },
  },
  nav: {
    flex: 1,
    minWidth: 0,
    display: 'flex',
    justifyContent: 'center',
    '@media (max-width: 1000px)': {
      display: 'none',
    },
  },
  userBadge: {
    '@media (max-width: 1000px)': {
      display: 'none',
    },
  },
  settingsButton: {
    '@media (max-width: 1000px)': {
      display: 'none',
    },
  },
  content: {
    flex: 1,
    minHeight: 0,
    position: 'relative',
  },
  mobileViewCloseButton: {
    display: 'none',
    position: 'absolute',
    top: tokens.spacingVerticalM,
    right: tokens.spacingHorizontalL,
    zIndex: 1,
    '@media (max-width: 1000px)': {
      display: 'inline-flex',
    },
  },
  mobileDrawer: {
    width: 'min(85vw, 320px)',
  },
  mobileNavList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalL,
  },
  mobileNavItem: {
    justifyContent: 'flex-start',
  },
  mobileDrawerBody: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
  },
  mobileUserInfo: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  mobileUserEmail: {
    overflowWrap: 'break-word',
  },
  mobileDrawerBranding: {
    display: 'none',
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalL,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    alignItems: 'center',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    '@media (max-width: 1000px)': {
      display: 'flex',
    },
  },
  mobileDrawerDiagLinkLogo: {
    height: '48px',
    width: 'auto',
    objectFit: 'contain',
  },
  mobileDrawerFoundry: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalS,
  },
  mobileDrawerFoundryLogo: {
    color: tokens.colorBrandForeground1,
  },
});

interface AppShellProps {
  agentId: string;
  agentName: string;
  agentDescription?: string;
  agentLogo?: string;
  starterPrompts?: string[];
  onDiagLinkSessionExpired?: () => void;
}

/** Main authenticated layout: header (brand + role-gated nav + user badge) and the routed view below it. */
export const AppShell: React.FC<AppShellProps> = ({
  agentId,
  agentName,
  agentDescription,
  agentLogo,
  starterPrompts,
  onDiagLinkSessionExpired,
}) => {
  const styles = useStyles();
  const { auth, ui, state, dispatch } = useAppState();
  const { getAccessToken } = useAuth();
  const currentUser = auth.currentUser;
  const currentView = ui.currentView;
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isSupportContactOpen, setIsSupportContactOpen] = useState(false);
  const [machineRequests, setMachineRequests] = useState<MachineRequestListItem[]>([]);
  const [machineRequestsLoading, setMachineRequestsLoading] = useState(false);
  const [machineRequestsError, setMachineRequestsError] = useState(false);

  const loadMachineRequests = useCallback(async () => {
    if (currentUser?.role !== 'diaglink_super_admin') {
      setMachineRequests([]);
      setMachineRequestsError(false);
      return;
    }
    setMachineRequestsLoading(true);
    setMachineRequestsError(false);
    const result = await listMachineRequests(getAccessToken);
    if (result.kind === 'success') setMachineRequests(result.data);
    else {
      setMachineRequestsError(true);
      if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) onDiagLinkSessionExpired?.();
    }
    setMachineRequestsLoading(false);
  }, [currentUser?.role, getAccessToken, onDiagLinkSessionExpired]);

  useEffect(() => { void loadMachineRequests(); }, [loadMachineRequests]);

  const handleMachineRequestUpdated = useCallback((request: MachineRequestDetail) => {
    setMachineRequests(current => current.map(item => item.requestId === request.requestId
      ? { ...item, status: request.status }
      : item));
  }, []);

  const pendingMachineRequestCount = machineRequests.filter(request => request.status === 'pending').length;

  useEffect(() => {
    // Keep the requested URL during login and while /auth/me is loading.
    if (!currentUser) return;
    const readRoute = () => {
      const administration = /^\/app\/administration\/?$/.test(window.location.pathname);
      const requested = administration ? 'diaglink-admin' : window.history.state?.diaglinkView;
      if (requested || /^\/app\/?$/.test(window.location.pathname)) {
        const view = resolveView(requested ?? 'chat', currentUser.role);
        if (administration && view !== 'diaglink-admin') {
          window.history.replaceState(null, '', '/app');
        }
        dispatch({ type: 'UI_SET_VIEW', view });
      }
    };
    if (/^\/app\/administration\/?$/.test(window.location.pathname)) readRoute();
    window.addEventListener('popstate', readRoute);
    return () => window.removeEventListener('popstate', readRoute);
  }, [currentUser, dispatch]);

  const handleSelectView = (view: AppView) => {
    const allowedView = resolveView(view, currentUser?.role);
    window.history.replaceState({ ...window.history.state, diaglinkView: ui.currentView }, '');
    window.history.pushState({ diaglinkView: allowedView }, '', allowedView === 'diaglink-admin' ? '/app/administration' : '/app');
    dispatch({ type: 'UI_SET_VIEW', view: allowedView });
  };

  const handleSelectMobileView = (view: AppView) => {
    handleSelectView(view);
    setIsMobileMenuOpen(false);
  };

  const handleOpenSettings = () => {
    setIsSettingsOpen(true);
    setIsMobileMenuOpen(false);
  };

  const handleOpenSupportContact = () => {
    setIsSupportContactOpen(true);
    setIsMobileMenuOpen(false);
  };

  const mobileNavItems = getNavItemsForRole(currentUser?.role, pendingMachineRequestCount);
  const canContactSupport = currentUser?.role === 'technician' || currentUser?.role === 'company_admin';
  const showMobileViewCloseButton = ['companies', 'users', 'machines', 'machine-requests', 'diaglink-admin'].includes(currentView);

  return (
    <div className={styles.shell}>
      <header className={styles.header}>
        <div className={styles.brand}>
          {/* Header brand logo removed per request (keep page logo intact) */}
        </div>
        <div className={styles.nav}>
          <Navigation role={currentUser?.role} currentView={currentView} onSelectView={handleSelectView} pendingMachineRequestCount={pendingMachineRequestCount} />
        </div>
        {canContactSupport && (
          <Button
            className={styles.settingsButton}
            appearance="subtle"
            icon={<Mail24Regular />}
            onClick={handleOpenSupportContact}
          >
            Contacter DiagLink
          </Button>
        )}
        <Button
          className={styles.settingsButton}
          appearance="subtle"
          icon={<Settings24Regular />}
          onClick={handleOpenSettings}
        >
          Paramètres
        </Button>
        <div className={styles.userBadge}>
          <UserBadge currentUser={currentUser} />
        </div>
      </header>

      <Drawer
        className={styles.mobileDrawer}
        open={isMobileMenuOpen}
        onOpenChange={(_, { open }) => setIsMobileMenuOpen(open)}
        position="start"
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            action={
              <Button
                appearance="subtle"
                aria-label="Fermer"
                icon={<Dismiss24Regular />}
                onClick={() => setIsMobileMenuOpen(false)}
              />
            }
          >
            Menu
          </DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody className={styles.mobileDrawerBody}>
          <div className={styles.mobileNavList}>
            {mobileNavItems.map(item => (
              <Button
                key={item.view}
                className={styles.mobileNavItem}
                appearance={currentView === item.view ? 'primary' : 'subtle'}
                onClick={() => handleSelectMobileView(item.view)}
              >
                {item.label}{item.badgeCount !== undefined && <Badge appearance="filled" color="important" size="small">{item.badgeCount}</Badge>}
              </Button>
            ))}
          </div>
          {canContactSupport && (
            <Button
              className={styles.mobileNavItem}
              appearance="subtle"
              icon={<Mail24Regular />}
              onClick={handleOpenSupportContact}
            >
              Contacter DiagLink
            </Button>
          )}
          <Button
            className={styles.mobileNavItem}
            appearance="subtle"
            icon={<Settings24Regular />}
            onClick={handleOpenSettings}
          >
            Paramètres
          </Button>
          {currentUser && (
            <div className={styles.mobileUserInfo}>
              <Text className={styles.mobileUserEmail}>
                {[currentUser.firstName, currentUser.lastName].filter(Boolean).join(' ') || currentUser.email}
              </Text>
              <Badge appearance="tint" color="informative">
                {ROLE_LABELS[currentUser.role]}
              </Badge>
            </div>
          )}
          <div className={styles.mobileDrawerBranding}>
            <img src={logoDiagLink} alt="DiagLink" className={styles.mobileDrawerDiagLinkLogo} />
            <div className={styles.mobileDrawerFoundry}>
              <Text size={200}>Propulsé par Microsoft Foundry</Text>
            </div>
          </div>
        </DrawerBody>
      </Drawer>

      <div className={styles.content}>
        {showMobileViewCloseButton && (
          <Button
            className={styles.mobileViewCloseButton}
            appearance="subtle"
            icon={<Dismiss24Regular />}
            aria-label="Fermer et revenir au chat"
            onClick={() => handleSelectView('chat')}
          />
        )}
        {(currentView === 'chat' || currentView === 'history') && (
          <AgentChat
            agentId={agentId}
            agentName={agentName}
            agentDescription={agentDescription}
            agentLogo={agentLogo}
            starterPrompts={starterPrompts}
            onDiagLinkSessionExpired={onDiagLinkSessionExpired}
            autoOpenHistory={currentView === 'history'}
            onOpenMobileMenu={() => setIsMobileMenuOpen(true)}
          />
        )}
        {currentView === 'machines' && (
          <MachinesView currentUser={currentUser} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} />
        )}
        {currentView === 'users' && (
          <UsersView currentUser={currentUser} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} />
        )}
        {currentView === 'company' && (
          <CompanyView machine={state.machine.selected ?? undefined} getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired}
            onReturnToChat={currentUser?.role === 'company_admin' ? () => handleSelectView('chat') : undefined} />
        )}
        {currentView === 'companies' && (
          <CompaniesView getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} />
        )}
        {currentView === 'machine-requests' && (
          <MachineRequestsView
            requests={machineRequests}
            isLoading={machineRequestsLoading}
            hasLoadingError={machineRequestsError}
            getAccessToken={getAccessToken}
            onRetry={loadMachineRequests}
            onRequestUpdated={handleMachineRequestUpdated}
            onDiagLinkSessionExpired={onDiagLinkSessionExpired}
          />
        )}
        {currentView === 'diaglink-admin' && <DiagLinkAdminView onDiagLinkSessionExpired={onDiagLinkSessionExpired} />}
      </div>
      {canContactSupport && (
        <SupportContactDialog
          open={isSupportContactOpen}
          onOpenChange={setIsSupportContactOpen}
          getAccessToken={getAccessToken}
          machineId={state.machine.selected?.id}
          onDiagLinkSessionExpired={onDiagLinkSessionExpired}
        />
      )}
      <SettingsPanel isOpen={isSettingsOpen} onOpenChange={setIsSettingsOpen} />
    </div>
  );
};
