import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Checkbox,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Field,
  Input,
  Dropdown,
  Option,
  Spinner,
} from '@fluentui/react-components';
import { ArrowLeftRegular, DeleteRegular } from '@fluentui/react-icons';
import { ViewRoot, ViewMessage } from './ViewLayout';
import { useApiResource } from '../../hooks/useApiResource';
import { getCompanies } from '../../services/companyService';
import { getCompanyUsers, getUsersByCompany, createTechnician, createUserForCompany, deleteCompanyUser, deleteUserForCompany, reactivateCompanyUser, reactivateUserForCompany, permanentlyDeleteCompanyUser, permanentlyDeleteUserForCompany, getUserMachineAccess, getUserMachineAccessForCompany, replaceUserMachineAccess, replaceUserMachineAccessForCompany } from '../../services/userService';
import { isSuperAdmin, isCompanyAdmin } from '../../utils/roles';
import type { CurrentUser, DiagLinkRole } from '../../types/currentUser';
import type { CompanyDto, CompanyUserDto } from '../../types/company';
import type { UserMachineAccessDto } from '../../types/machine';

const ROLE_LABELS: Record<DiagLinkRole, string> = {
  technician: 'Technicien',
  company_admin: 'Administrateur entreprise',
  diaglink_super_admin: 'Super administrateur DiagLink',
};

// Roles assignable from this view — diaglink_super_admin is never offered here.
const ASSIGNABLE_ROLES: DiagLinkRole[] = ['technician', 'company_admin'];


const useStyles = makeStyles({
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: tokens.spacingVerticalM,
  },
  table: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    width: '100%',
    overflowX: 'auto',
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '1.4fr 2fr 1.2fr 1.5fr 0.8fr 80px',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    alignItems: 'center',
    width: '100%',
    boxSizing: 'border-box',
    // Below 768px the fixed columns no longer fit the viewport (that's what was pushing "Actions"/
    // "Gérer" out of frame) — stack every cell as its own full-width row instead.
    '@media (max-width: 768px)': {
      gridTemplateColumns: '1fr',
      rowGap: tokens.spacingVerticalXS,
      '> *': {
        minWidth: 0,
        overflowWrap: 'anywhere',
      },
    },
  },
  rowClickable: {
    cursor: 'pointer',
  },
  rowSelected: {
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  headerRow: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  // Applied only to the technician/company_admin list headers (row + superAdminUserRow) — the
  // company list header keeps its own layout and isn't affected.
  userTableHeader: {
    '@media (max-width: 768px)': {
      display: 'none',
    },
  },
  companyListContainer: {
    width: '100%',
    maxWidth: '1200px',
    marginLeft: 'auto',
    marginRight: 'auto',
  },
  companyRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(240px, 1fr) 160px 160px',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    alignItems: 'center',
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    '@media (max-width: 768px)': {
      minWidth: '480px',
    },
  },
  companyHeaderRow: {
    cursor: 'default',
    ':hover': {
      backgroundColor: 'transparent',
    },
  },
  companyStatus: {
    justifySelf: 'start',
  },
  companyUserCount: {
    justifySelf: 'start',
  },
  superAdminUserRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(180px, 1fr) minmax(220px, 1.25fr) 160px 180px 120px 80px',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    alignItems: 'center',
    width: '100%',
    boxSizing: 'border-box',
    '@media (max-width: 768px)': {
      gridTemplateColumns: '1fr',
      rowGap: tokens.spacingVerticalXS,
      '> *': {
        minWidth: 0,
        overflowWrap: 'anywhere',
      },
    },
  },
  superAdminUserStatus: {
    justifySelf: 'start',
  },
  backButton: {
    marginBottom: tokens.spacingVerticalM,
  },
  superAdminActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalM,
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  formError: {
    color: tokens.colorPaletteRedForeground1,
  },
  detailPanel: {
    marginTop: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  machineList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  machineLabel: {
    display: 'flex',
    flexDirection: 'column',
  },
  machineSecondary: {
    color: tokens.colorNeutralForeground3,
  },
  dangerButton: {
    color: tokens.colorPaletteRedForeground1,
  },
  actionsCell: {
    display: 'flex',
    justifyContent: 'flex-end',
    minWidth: 0,
  },
  expandedPanel: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 3fr) minmax(0, 2fr)',
    alignItems: 'start',
    gap: tokens.spacingHorizontalL,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    width: '100%',
    boxSizing: 'border-box',
    '@media (max-width: 480px)': {
      gap: tokens.spacingHorizontalS,
      padding: tokens.spacingVerticalS,
    },
  },
  expandedPanelColumn: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalS,
    minWidth: 0,
    wordBreak: 'break-word',
  },
  expandedPanelMachinesColumn: {
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingRight: tokens.spacingHorizontalL,
    '@media (max-width: 480px)': {
      paddingRight: tokens.spacingHorizontalS,
    },
  },
  machineListScroll: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    width: '100%',
    maxHeight: '260px',
    overflowY: 'auto',
  },
  fullWidthButton: {
    width: '100%',
  },
});

interface UsersViewProps {
  currentUser: CurrentUser | null;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}

export const UsersView: React.FC<UsersViewProps> = ({ currentUser, getAccessToken, onDiagLinkSessionExpired }) => {
  const styles = useStyles();
  const isSuper = isSuperAdmin(currentUser);
  // Only company_admin manages technicians/machine access — defense in depth even though the
  // navigation already hides this view's write features from technician (see utils/navigation.ts).
  const isAdmin = isCompanyAdmin(currentUser);

  // Mirrors the backend allow-list: diaglink_super_admin can never be deleted here, and nobody can
  // delete their own account — the backend re-validates both regardless of this frontend check.
  const canDeleteUser = useCallback(
    (user: CompanyUserDto) => user.role !== 'diaglink_super_admin' && user.id !== currentUser?.userId,
    [currentUser]
  );

  const [selectedUser, setSelectedUser] = useState<CompanyUserDto | null>(null);
  const [selectedCompany, setSelectedCompany] = useState<CompanyDto | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [superAdminRefreshKey, setSuperAdminRefreshKey] = useState(0);
  const [usersByCompany, setUsersByCompany] = useState<Record<string, CompanyUserDto[]>>({});
  const [superAdminUsersLoading, setSuperAdminUsersLoading] = useState(false);
  const [superAdminUsersError, setSuperAdminUsersError] = useState(false);
  const [superAdminDialogOpen, setSuperAdminDialogOpen] = useState(false);
  const [superAdminEmail, setSuperAdminEmail] = useState('');
  const [superAdminFirstName, setSuperAdminFirstName] = useState('');
  const [superAdminLastName, setSuperAdminLastName] = useState('');
  const [superAdminPhoneNumber, setSuperAdminPhoneNumber] = useState('');
  const [superAdminRole, setSuperAdminRole] = useState<DiagLinkRole>('technician');
  const [superAdminSubmitting, setSuperAdminSubmitting] = useState(false);
  const [superAdminFormError, setSuperAdminFormError] = useState<string | null>(null);
  const [superAdminConfirmation, setSuperAdminConfirmation] = useState<string | null>(null);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [role, setRole] = useState<DiagLinkRole>('technician');
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);

  const [machines, setMachines] = useState<UserMachineAccessDto[] | null>(null);
  const [checkedMachineIds, setCheckedMachineIds] = useState<Set<string>>(new Set());
  const [machinesLoading, setMachinesLoading] = useState(false);
  const [machinesError, setMachinesError] = useState<string | null>(null);
  const [savingAccess, setSavingAccess] = useState(false);
  const [accessConfirmation, setAccessConfirmation] = useState<string | null>(null);
  const [machineDialogOpen, setMachineDialogOpen] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<CompanyUserDto | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const [reactivatingUserId, setReactivatingUserId] = useState<string | null>(null);
  const [reactivateError, setReactivateError] = useState<string | null>(null);

  const [permanentDeleteTarget, setPermanentDeleteTarget] = useState<CompanyUserDto | null>(null);
  const [permanentlyDeleting, setPermanentlyDeleting] = useState(false);
  const [permanentDeleteError, setPermanentDeleteError] = useState<string | null>(null);

  // Single row expanded at a time — reuses `selectedUser` (kept in sync below) to drive the existing
  // machine-access fetch effect, so expanding a row is exactly "select this user" for that purpose.
  const [expandedUserId, setExpandedUserId] = useState<string | null>(null);

  const toggleExpandedUser = (user: CompanyUserDto) => {
    setExpandedUserId(prev => {
      const next = prev === user.id ? null : user.id;
      setSelectedUser(next ? user : null);
      return next;
    });
  };

  // Tracks whichever row/panel pair is currently expanded, so an outside click can tell it apart
  // from a click inside the panel's own controls (checkboxes, buttons, machine list).
  const expandedRowRef = useRef<HTMLDivElement | null>(null);
  const expandedPanelRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!expandedUserId) return;

    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (expandedRowRef.current?.contains(target) || expandedPanelRef.current?.contains(target)) {
        return;
      }
      setExpandedUserId(null);
      setSelectedUser(null);
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [expandedUserId]);

  // GET /api/company/users stays scoped to the caller's own company — never a global listing.
  // For diaglink_super_admin this endpoint is intentionally not called (see below).
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const fetchUsers = useCallback(() => getCompanyUsers(getAccessToken), [getAccessToken, refreshKey]);
  const state = useApiResource(fetchUsers, onDiagLinkSessionExpired, isAdmin);
  const fetchCompanies = useCallback(() => getCompanies(getAccessToken), [getAccessToken]);
  const companiesState = useApiResource(fetchCompanies, onDiagLinkSessionExpired, isSuper);

  useEffect(() => {
    if (!isSuper || companiesState.kind !== 'success') return;

    let cancelled = false;
    setSuperAdminUsersLoading(true);
    setSuperAdminUsersError(false);

    Promise.all(companiesState.data.map(async company => ({
      companyId: company.id,
      result: await getUsersByCompany(getAccessToken, company.id),
    }))).then(results => {
      if (cancelled) return;

      if (results.some(({ result }) => result.kind === 'unauthorized' && result.diagLinkSessionExpired)) {
        onDiagLinkSessionExpired?.();
      }

      const failed = results.some(({ result }) => result.kind !== 'success');
      const users = results.reduce<Record<string, CompanyUserDto[]>>((byCompany, { companyId, result }) => {
        byCompany[companyId] = result.kind === 'success' ? result.data : [];
        return byCompany;
      }, {});

      setUsersByCompany(users);
      setSuperAdminUsersError(failed);
      setSuperAdminUsersLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [isSuper, companiesState, getAccessToken, onDiagLinkSessionExpired, superAdminRefreshKey]);

  useEffect(() => {
    if (!selectedUser || selectedUser.role !== 'technician') {
      setMachines(null);
      setMachinesError(null);
      setAccessConfirmation(null);
      return;
    }

    let cancelled = false;
    setMachinesLoading(true);
    setMachinesError(null);
    setAccessConfirmation(null);

    const request = isSuper && selectedCompany
      ? getUserMachineAccessForCompany(getAccessToken, selectedCompany.id, selectedUser.id)
      : getUserMachineAccess(getAccessToken, selectedUser.id);

    request.then(result => {
      if (cancelled) return;
      setMachinesLoading(false);

      if (result.kind === 'success') {
        setMachines(result.data);
        setCheckedMachineIds(new Set(result.data.filter(m => m.assigned).map(m => m.machineId)));
        return;
      }

      setMachines(null);
      setMachinesError('Impossible de charger les machines.');
    });

    return () => {
      cancelled = true;
    };
  }, [selectedUser, getAccessToken, isSuper, selectedCompany]);

  const resetForm = () => {
    setEmail('');
    setFirstName('');
    setLastName('');
    setPhoneNumber('');
    setRole('technician');
    setFormError(null);
  };

  const openDialog = () => {
    resetForm();
    setDialogOpen(true);
  };

  const handleCreateTechnician = async () => {
    if (submitting) return;

    const trimmedEmail = email.trim();
    const trimmedFirstName = firstName.trim();
    const trimmedLastName = lastName.trim();
    const trimmedPhoneNumber = phoneNumber.trim();

    if (!trimmedFirstName) {
      setFormError('Le prénom est obligatoire.');
      return;
    }
    if (!trimmedLastName) {
      setFormError('Le nom est obligatoire.');
      return;
    }
    if (!trimmedEmail) {
      setFormError("L'email est obligatoire.");
      return;
    }
    if (!trimmedPhoneNumber) {
      setFormError('Le téléphone est obligatoire.');
      return;
    }
    if (!role) {
      setFormError('Le rôle est obligatoire.');
      return;
    }

    setSubmitting(true);
    setFormError(null);

    const result = await createTechnician(getAccessToken, {
      email: trimmedEmail,
      firstName: trimmedFirstName,
      lastName: trimmedLastName,
      phoneNumber: trimmedPhoneNumber,
      role,
    });

    setSubmitting(false);

    if (result.kind === 'success') {
      setDialogOpen(false);
      resetForm();
      setConfirmation('Utilisateur ajouté.');
      setRefreshKey(k => k + 1);
      setSelectedUser(result.data);
      setMachineDialogOpen(true);
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setFormError(result.message);
      return;
    }

    if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
      setFormError('Accès non autorisé.');
      return;
    }

    setFormError("Impossible d'ajouter l'utilisateur.");
  };

  const openSuperAdminDialog = () => {
    setSuperAdminEmail('');
    setSuperAdminFirstName('');
    setSuperAdminLastName('');
    setSuperAdminPhoneNumber('');
    setSuperAdminRole('technician');
    setSuperAdminFormError(null);
    setSuperAdminDialogOpen(true);
  };

  const handleCreateUserForCompany = async () => {
    if (!selectedCompany || superAdminSubmitting) return;

    const trimmedEmail = superAdminEmail.trim();
    const trimmedFirstName = superAdminFirstName.trim();
    const trimmedLastName = superAdminLastName.trim();
    const trimmedPhoneNumber = superAdminPhoneNumber.trim();

    if (!trimmedFirstName) {
      setSuperAdminFormError('Le prénom est obligatoire.');
      return;
    }
    if (!trimmedLastName) {
      setSuperAdminFormError('Le nom est obligatoire.');
      return;
    }
    if (!trimmedEmail) {
      setSuperAdminFormError("L'email est obligatoire.");
      return;
    }
    if (!trimmedPhoneNumber) {
      setSuperAdminFormError('Le téléphone est obligatoire.');
      return;
    }
    if (!superAdminRole) {
      setSuperAdminFormError('Le rôle est obligatoire.');
      return;
    }

    setSuperAdminSubmitting(true);
    setSuperAdminFormError(null);
    const result = await createUserForCompany(getAccessToken, selectedCompany.id, {
      email: trimmedEmail,
      firstName: trimmedFirstName,
      lastName: trimmedLastName,
      phoneNumber: trimmedPhoneNumber,
      role: superAdminRole,
    });
    setSuperAdminSubmitting(false);

    if (result.kind === 'success') {
      setSuperAdminDialogOpen(false);
      setSuperAdminEmail('');
      setSuperAdminFirstName('');
      setSuperAdminLastName('');
      setSuperAdminPhoneNumber('');
      setSuperAdminRole('technician');
      setSuperAdminFormError(null);
      setSuperAdminConfirmation('Utilisateur ajouté.');
      setSuperAdminRefreshKey(key => key + 1);
      setSelectedUser(result.data);
      setMachineDialogOpen(true);
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
      setSuperAdminFormError('Accès non autorisé.');
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setSuperAdminFormError(result.message);
      return;
    }

    setSuperAdminFormError("Impossible d'ajouter l'utilisateur.");
  };

  const toggleMachine = (machineId: string) => {
    setCheckedMachineIds(prev => {
      const next = new Set(prev);
      if (next.has(machineId)) {
        next.delete(machineId);
      } else {
        next.add(machineId);
      }
      return next;
    });
  };

  const openDeleteDialog = (user: CompanyUserDto) => {
    setDeleteError(null);
    setDeleteTarget(user);
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget || deleting) return;

    setDeleting(true);
    setDeleteError(null);

    const result = isSuper && selectedCompany
      ? await deleteUserForCompany(getAccessToken, selectedCompany.id, deleteTarget.id)
      : await deleteCompanyUser(getAccessToken, deleteTarget.id);

    setDeleting(false);

    if (result.kind === 'success') {
      if (selectedUser?.id === deleteTarget.id) {
        setSelectedUser(null);
      }
      setDeleteTarget(null);
      if (isSuper && selectedCompany) {
        setSuperAdminRefreshKey(key => key + 1);
      } else {
        setRefreshKey(k => k + 1);
      }
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
      setDeleteError('Accès non autorisé.');
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setDeleteError(result.message);
      return;
    }

    setDeleteError("Impossible de désactiver l'accès.");
  };

  const handleReactivateUser = async (user: CompanyUserDto) => {
    if (reactivatingUserId) return;

    setReactivatingUserId(user.id);
    setReactivateError(null);

    const result = isSuper && selectedCompany
      ? await reactivateUserForCompany(getAccessToken, selectedCompany.id, user.id)
      : await reactivateCompanyUser(getAccessToken, user.id);

    setReactivatingUserId(null);

    if (result.kind === 'success') {
      if (isSuper && selectedCompany) {
        setSuperAdminRefreshKey(key => key + 1);
      } else {
        setRefreshKey(k => k + 1);
      }
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
      setReactivateError('Accès non autorisé.');
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setReactivateError(result.message);
      return;
    }

    setReactivateError("Impossible de réactiver l'accès.");
  };

  const openPermanentDeleteDialog = (user: CompanyUserDto) => {
    setPermanentDeleteError(null);
    setPermanentDeleteTarget(user);
  };

  const handleConfirmPermanentDelete = async () => {
    if (!permanentDeleteTarget || permanentlyDeleting) return;

    setPermanentlyDeleting(true);
    setPermanentDeleteError(null);

    const result = isSuper && selectedCompany
      ? await permanentlyDeleteUserForCompany(getAccessToken, selectedCompany.id, permanentDeleteTarget.id)
      : await permanentlyDeleteCompanyUser(getAccessToken, permanentDeleteTarget.id);

    setPermanentlyDeleting(false);

    if (result.kind === 'success') {
      if (expandedUserId === permanentDeleteTarget.id) {
        setExpandedUserId(null);
        setSelectedUser(null);
      }
      setPermanentDeleteTarget(null);
      if (isSuper && selectedCompany) {
        setSuperAdminRefreshKey(key => key + 1);
      } else {
        setRefreshKey(k => k + 1);
      }
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
      setPermanentDeleteError('Accès non autorisé.');
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setPermanentDeleteError(result.message);
      return;
    }

    setPermanentDeleteError("Impossible de supprimer définitivement l'utilisateur.");
  };

  const handleSaveAccess = async () => {
    if (!selectedUser || savingAccess) return;

    setSavingAccess(true);
    setMachinesError(null);
    setAccessConfirmation(null);

    const result = isSuper && selectedCompany
      ? await replaceUserMachineAccessForCompany(getAccessToken, selectedCompany.id, selectedUser.id, Array.from(checkedMachineIds))
      : await replaceUserMachineAccess(getAccessToken, selectedUser.id, Array.from(checkedMachineIds));

    setSavingAccess(false);

    if (result.kind === 'success') {
      setAccessConfirmation('Accès machines mis à jour.');
      return;
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setMachinesError(result.message);
      return;
    }

    setMachinesError('Impossible de mettre à jour les accès.');
  };

  if (isSuper) {
    const selectedCompanyUsers = selectedCompany ? usersByCompany[selectedCompany.id] ?? [] : [];

    return (
      <ViewRoot
        title={selectedCompany ? `Utilisateurs — ${selectedCompany.name}` : 'Utilisateurs'}
        subtitle="Gérez les utilisateurs DiagLink."
      >
        {!selectedCompany && companiesState.kind === 'loading' && <ViewMessage loading message="Chargement des entreprises..." />}
        {!selectedCompany && companiesState.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
        {!selectedCompany && companiesState.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
        {!selectedCompany && companiesState.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
        {!selectedCompany && companiesState.kind === 'success' && superAdminUsersLoading && <ViewMessage loading message="Chargement des utilisateurs..." />}
        {!selectedCompany && companiesState.kind === 'success' && !superAdminUsersLoading && superAdminUsersError && <ViewMessage message="Impossible de charger les données." />}
        {!selectedCompany && companiesState.kind === 'success' && !superAdminUsersLoading && !superAdminUsersError && (
          <div className={styles.companyListContainer}>
            <div className={styles.table}>
              <div className={`${styles.companyRow} ${styles.headerRow} ${styles.companyHeaderRow}`}>
                <Text>Entreprise</Text>
                <Text>Utilisateurs</Text>
                <Text>Statut</Text>
              </div>
              {companiesState.data.map(company => {
                const userCount = usersByCompany[company.id]?.length ?? 0;
                return (
                  <div
                    key={company.id}
                    role="button"
                    tabIndex={0}
                    className={styles.companyRow}
                    onClick={() => setSelectedCompany(company)}
                    onKeyDown={event => {
                      if (event.key === 'Enter') setSelectedCompany(company);
                    }}
                  >
                    <Text>{company.name}</Text>
                    <Text className={styles.companyUserCount}>{userCount}</Text>
                    <Text className={styles.companyStatus}>{company.status}</Text>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {selectedCompany && superAdminUsersLoading && <ViewMessage loading message="Chargement des utilisateurs..." />}
        {selectedCompany && !superAdminUsersLoading && superAdminUsersError && <ViewMessage message="Impossible de charger les données." />}
        {selectedCompany && !superAdminUsersLoading && !superAdminUsersError && (
          <div className={styles.companyListContainer}>
            <div className={styles.superAdminActions}>
              <Button
                appearance="secondary"
                className={styles.backButton}
                icon={<ArrowLeftRegular />}
                onClick={() => setSelectedCompany(null)}
              >
                Retour aux entreprises
              </Button>
              <Button appearance="primary" className={styles.backButton} onClick={openSuperAdminDialog}>
                Ajouter un utilisateur
              </Button>
            </div>
            {superAdminConfirmation && <Text>{superAdminConfirmation}</Text>}
            {selectedCompanyUsers.length === 0 ? (
              <ViewMessage message="Aucun utilisateur trouvé." />
            ) : (
              <div className={styles.table}>
                <div className={`${styles.superAdminUserRow} ${styles.headerRow} ${styles.userTableHeader}`}>
                  <Text>Nom</Text>
                  <Text>Email</Text>
                  <Text>Téléphone</Text>
                  <Text>Rôle</Text>
                  <Text>Statut</Text>
                  <Text>Actions</Text>
                </div>
                {selectedCompanyUsers.map(user => (
                  <React.Fragment key={user.id}>
                    <div
                      ref={expandedUserId === user.id ? expandedRowRef : undefined}
                      role="button"
                      tabIndex={0}
                      className={`${styles.superAdminUserRow} ${styles.rowClickable} ${expandedUserId === user.id ? styles.rowSelected : ''}`}
                      onClick={() => toggleExpandedUser(user)}
                      onKeyDown={event => {
                        if (event.key === 'Enter') toggleExpandedUser(user);
                      }}
                    >
                      <Text>{[user.firstName, user.lastName].filter(Boolean).join(' ') || '—'}</Text>
                      <Text>{user.email ?? '—'}</Text>
                      <Text>{user.phoneNumber ?? '—'}</Text>
                      <Text>{ROLE_LABELS[user.role as DiagLinkRole] ?? user.role ?? '—'}</Text>
                      <Text className={styles.superAdminUserStatus}>{user.status ?? '—'}</Text>
                      <div className={styles.actionsCell}>
                        <Button
                          appearance="subtle"
                          size="small"
                          onClick={event => {
                            event.stopPropagation();
                            toggleExpandedUser(user);
                          }}
                        >
                          Gérer
                        </Button>
                      </div>
                    </div>
                    {expandedUserId === user.id && (
                      <div ref={expandedPanelRef} className={styles.expandedPanel}>
                        <div className={`${styles.expandedPanelColumn} ${styles.expandedPanelMachinesColumn}`}>
                          {user.role === 'technician' && (
                            <>
                              <Text weight="semibold">Gestion des machines</Text>
                              {machinesLoading && <ViewMessage loading message="Chargement des machines..." />}
                              {machinesError && <Text className={styles.formError}>{machinesError}</Text>}
                              {!machinesLoading && machines && machines.length === 0 && (
                                <Text>Aucune machine disponible pour cette entreprise.</Text>
                              )}
                              {!machinesLoading && machines && machines.length > 0 && (
                                <div className={styles.machineListScroll}>
                                  {machines.map(machine => (
                                    <Checkbox
                                      key={machine.machineId}
                                      checked={checkedMachineIds.has(machine.machineId)}
                                      onChange={() => toggleMachine(machine.machineId)}
                                      disabled={savingAccess}
                                      label={<div className={styles.machineLabel}><Text>{machine.name}</Text>{machine.reference && <Text size={200} className={styles.machineSecondary}>{machine.reference}</Text>}</div>}
                                    />
                                  ))}
                                </div>
                              )}
                              <Button appearance="primary" onClick={handleSaveAccess} disabled={savingAccess || machinesLoading || !machines}>
                                {savingAccess ? <Spinner size="tiny" /> : 'Enregistrer les accès'}
                              </Button>
                              {accessConfirmation && <Text>{accessConfirmation}</Text>}
                            </>
                          )}
                        </div>
                        <div className={styles.expandedPanelColumn}>
                          {canDeleteUser(user) && (
                            <>
                              <Text weight="semibold">Gestion du compte</Text>
                              <Text>Statut : {user.status ?? '—'}</Text>
                              {user.status === 'active' ? (
                                <Button
                                  appearance="subtle"
                                  icon={<DeleteRegular />}
                                  className={`${styles.dangerButton} ${styles.fullWidthButton}`}
                                  onClick={() => openDeleteDialog(user)}
                                >
                                  Désactiver l'accès
                                </Button>
                              ) : (
                                <Button
                                  appearance="subtle"
                                  className={styles.fullWidthButton}
                                  disabled={reactivatingUserId === user.id}
                                  onClick={() => handleReactivateUser(user)}
                                >
                                  {reactivatingUserId === user.id ? <Spinner size="tiny" /> : "Réactiver l'accès"}
                                </Button>
                              )}
                              {reactivateError && <Text className={styles.formError}>{reactivateError}</Text>}
                              <Button
                                appearance="outline"
                                icon={<DeleteRegular />}
                                className={`${styles.dangerButton} ${styles.fullWidthButton}`}
                                onClick={() => openPermanentDeleteDialog(user)}
                              >
                                Supprimer l'utilisateur
                              </Button>
                            </>
                          )}
                        </div>
                      </div>
                    )}
                  </React.Fragment>
                ))}

              </div>
            )}
          </div>
        )}
        <Dialog open={superAdminDialogOpen} onOpenChange={(_event, data) => !superAdminSubmitting && setSuperAdminDialogOpen(data.open)}>
          <DialogSurface>
            <DialogTitle>Ajouter un utilisateur</DialogTitle>
            <DialogBody>
              <DialogContent className={styles.form}>
                <Field label="Prénom" required>
                  <Input
                    value={superAdminFirstName}
                    onChange={(_event, data) => setSuperAdminFirstName(data.value)}
                    disabled={superAdminSubmitting}
                  />
                </Field>
                <Field label="Nom" required>
                  <Input
                    value={superAdminLastName}
                    onChange={(_event, data) => setSuperAdminLastName(data.value)}
                    disabled={superAdminSubmitting}
                  />
                </Field>
                <Field label="Email de l'utilisateur" required>
                  <Input
                    type="email"
                    value={superAdminEmail}
                    onChange={(_event, data) => setSuperAdminEmail(data.value)}
                    disabled={superAdminSubmitting}
                  />
                </Field>
                <Field label="Téléphone" required>
                  <Input
                    type="tel"
                    value={superAdminPhoneNumber}
                    onChange={(_event, data) => setSuperAdminPhoneNumber(data.value)}
                    disabled={superAdminSubmitting}
                  />
                </Field>
                <Field label="Rôle" required>
                  <Dropdown
                    value={ROLE_LABELS[superAdminRole]}
                    selectedOptions={[superAdminRole]}
                    disabled={superAdminSubmitting}
                    onOptionSelect={(_event, data) => data.optionValue && setSuperAdminRole(data.optionValue as DiagLinkRole)}
                  >
                    {ASSIGNABLE_ROLES.map(r => (
                      <Option key={r} value={r}>
                        {ROLE_LABELS[r]}
                      </Option>
                    ))}
                  </Dropdown>
                </Field>
                {superAdminFormError && <Text className={styles.formError}>{superAdminFormError}</Text>}
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" disabled={superAdminSubmitting} onClick={() => setSuperAdminDialogOpen(false)}>
                  Annuler
                </Button>
                <Button appearance="primary" disabled={superAdminSubmitting} onClick={handleCreateUserForCompany}>
                  {superAdminSubmitting ? <Spinner size="tiny" /> : "Ajouter l'utilisateur"}
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
        <Dialog open={machineDialogOpen} onOpenChange={(_event, data) => !savingAccess && setMachineDialogOpen(data.open)}>
          <DialogSurface>
            <DialogTitle>Machines autorisées — {selectedUser?.email}</DialogTitle>
            <DialogBody>
              <DialogContent className={styles.form}>
                {machinesLoading && <ViewMessage loading message="Chargement des machines..." />}
                {machinesError && <Text className={styles.formError}>{machinesError}</Text>}
                {!machinesLoading && machines && machines.length === 0 && <Text>Aucune machine disponible pour cette entreprise.</Text>}
                {!machinesLoading && machines && machines.length > 0 && (
                  <div className={styles.machineList}>
                    {machines.map(machine => (
                      <Checkbox
                        key={machine.machineId}
                        checked={checkedMachineIds.has(machine.machineId)}
                        onChange={() => toggleMachine(machine.machineId)}
                        disabled={savingAccess}
                        label={<div className={styles.machineLabel}><Text>{machine.name}</Text>{machine.reference && <Text size={200} className={styles.machineSecondary}>{machine.reference}</Text>}</div>}
                      />
                    ))}
                    {accessConfirmation && <Text>{accessConfirmation}</Text>}
                  </div>
                )}
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" disabled={savingAccess} onClick={() => setMachineDialogOpen(false)}>Annuler</Button>
                <Button appearance="primary" disabled={savingAccess || machinesLoading || !machines} onClick={handleSaveAccess}>
                  {savingAccess ? <Spinner size="tiny" /> : 'Enregistrer'}
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
        <Dialog open={!!deleteTarget} onOpenChange={(_event, data) => !deleting && !data.open && setDeleteTarget(null)}>
          <DialogSurface>
            <DialogTitle>Désactiver l'accès ?</DialogTitle>
            <DialogBody>
              <DialogContent className={styles.form}>
                <Text weight="semibold">{[deleteTarget?.firstName, deleteTarget?.lastName].filter(Boolean).join(' ') || deleteTarget?.email}</Text>
                <Text>{deleteTarget?.email}</Text>
                <Text className={styles.machineSecondary}>Cette action supprimera l'accès de cet utilisateur à DiagLink.</Text>
                {deleteError && <Text className={styles.formError}>{deleteError}</Text>}
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" disabled={deleting} onClick={() => setDeleteTarget(null)}>
                  Annuler
                </Button>
                <Button appearance="primary" className={styles.dangerButton} disabled={deleting} onClick={handleConfirmDelete}>
                  {deleting ? <Spinner size="tiny" /> : 'Désactiver'}
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
        <Dialog open={!!permanentDeleteTarget} onOpenChange={(_event, data) => !permanentlyDeleting && !data.open && setPermanentDeleteTarget(null)}>
          <DialogSurface>
            <DialogTitle>Supprimer définitivement cet utilisateur ?</DialogTitle>
            <DialogBody>
              <DialogContent className={styles.form}>
                <Text weight="semibold">{[permanentDeleteTarget?.firstName, permanentDeleteTarget?.lastName].filter(Boolean).join(' ') || permanentDeleteTarget?.email}</Text>
                <Text>{permanentDeleteTarget?.email}</Text>
                <Text className={styles.machineSecondary}>Cette action est définitive. L'utilisateur sera supprimé de DiagLink et ne pourra pas être restauré.</Text>
                {permanentDeleteError && <Text className={styles.formError}>{permanentDeleteError}</Text>}
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" disabled={permanentlyDeleting} onClick={() => setPermanentDeleteTarget(null)}>
                  Annuler
                </Button>
                <Button appearance="primary" className={styles.dangerButton} disabled={permanentlyDeleting} onClick={handleConfirmPermanentDelete}>
                  {permanentlyDeleting ? <Spinner size="tiny" /> : 'Supprimer définitivement'}
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
      </ViewRoot>
    );
  }

  if (!isAdmin) {
    return (
      <ViewRoot title="Utilisateurs" subtitle="Gérez les utilisateurs de votre entreprise.">
        <ViewMessage message="Accès non autorisé." />
      </ViewRoot>
    );
  }

  return (
    <ViewRoot title="Utilisateurs" subtitle="Gérez les utilisateurs de votre entreprise.">
      <div className={styles.header}>
        {confirmation && <Text>{confirmation}</Text>}
        <Button appearance="primary" onClick={openDialog} style={{ marginLeft: 'auto' }}>
          Ajouter un utilisateur
        </Button>
      </div>

      {state.kind === 'loading' && <ViewMessage loading message="Chargement des utilisateurs..." />}
      {state.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {state.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {state.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {state.kind === 'success' && state.data.length === 0 && <ViewMessage message="Aucun utilisateur trouvé." />}
      {state.kind === 'success' && state.data.length > 0 && (
        <div className={styles.table}>
          <div className={`${styles.row} ${styles.headerRow} ${styles.userTableHeader}`}>
            <Text>Nom</Text>
            <Text>Email</Text>
            <Text>Téléphone</Text>
            <Text>Rôle</Text>
            <Text>Statut</Text>
            <Text>Actions</Text>
          </div>
          {state.data.map(user => (
            <React.Fragment key={user.id}>
              <div
                ref={expandedUserId === user.id ? expandedRowRef : undefined}
                role="button"
                tabIndex={0}
                className={`${styles.row} ${styles.rowClickable} ${expandedUserId === user.id ? styles.rowSelected : ''}`}
                onClick={() => toggleExpandedUser(user)}
                onKeyDown={event => {
                  if (event.key === 'Enter') toggleExpandedUser(user);
                }}
              >
                <Text>{[user.firstName, user.lastName].filter(Boolean).join(' ') || '—'}</Text>
                <Text>{user.email ?? '—'}</Text>
                <Text>{user.phoneNumber ?? '—'}</Text>
                <Text>{ROLE_LABELS[user.role as DiagLinkRole] ?? user.role ?? '—'}</Text>
                <Text>{user.status ?? '—'}</Text>
                <div className={styles.actionsCell}>
                  <Button
                    appearance="subtle"
                    size="small"
                    onClick={event => {
                      event.stopPropagation();
                      toggleExpandedUser(user);
                    }}
                  >
                    Gérer
                  </Button>
                </div>
              </div>
              {expandedUserId === user.id && (
                <div ref={expandedPanelRef} className={styles.expandedPanel}>
                  <div className={`${styles.expandedPanelColumn} ${styles.expandedPanelMachinesColumn}`}>
                    {user.role === 'technician' && (
                      <>
                        <Text weight="semibold">Gestion des machines</Text>
                        {machinesLoading && <ViewMessage loading message="Chargement des machines..." />}
                        {machinesError && <Text className={styles.formError}>{machinesError}</Text>}
                        {!machinesLoading && machines && machines.length === 0 && (
                          <Text>Aucune machine disponible pour votre entreprise.</Text>
                        )}
                        {!machinesLoading && machines && machines.length > 0 && (
                          <div className={styles.machineListScroll}>
                            {machines.map(machine => (
                              <Checkbox
                                key={machine.machineId}
                                checked={checkedMachineIds.has(machine.machineId)}
                                onChange={() => toggleMachine(machine.machineId)}
                                disabled={savingAccess}
                                label={
                                  <div className={styles.machineLabel}>
                                    <Text>{machine.name}</Text>
                                    {(machine.reference) && (
                                      <Text size={200} className={styles.machineSecondary}>
                                        {machine.reference}
                                      </Text>
                                    )}
                                  </div>
                                }
                              />
                            ))}
                          </div>
                        )}
                        <Button appearance="primary" onClick={handleSaveAccess} disabled={savingAccess || machinesLoading || !machines}>
                          {savingAccess ? <Spinner size="tiny" /> : 'Enregistrer les accès'}
                        </Button>
                        {accessConfirmation && <Text>{accessConfirmation}</Text>}
                      </>
                    )}
                  </div>
                  <div className={styles.expandedPanelColumn}>
                    {canDeleteUser(user) && (
                      <>
                        <Text weight="semibold">Gestion du compte</Text>
                        <Text>Statut : {user.status ?? '—'}</Text>
                        {user.status === 'active' ? (
                          <Button
                            appearance="subtle"
                            icon={<DeleteRegular />}
                            className={`${styles.dangerButton} ${styles.fullWidthButton}`}
                            onClick={() => openDeleteDialog(user)}
                          >
                            Désactiver l'accès
                          </Button>
                        ) : (
                          <Button
                            appearance="subtle"
                            className={styles.fullWidthButton}
                            disabled={reactivatingUserId === user.id}
                            onClick={() => handleReactivateUser(user)}
                          >
                            {reactivatingUserId === user.id ? <Spinner size="tiny" /> : "Réactiver l'accès"}
                          </Button>
                        )}
                        {reactivateError && <Text className={styles.formError}>{reactivateError}</Text>}
                        <Button
                          appearance="outline"
                          icon={<DeleteRegular />}
                          className={`${styles.dangerButton} ${styles.fullWidthButton}`}
                          onClick={() => openPermanentDeleteDialog(user)}
                        >
                          Supprimer l'utilisateur
                        </Button>
                      </>
                    )}
                  </div>
                </div>
              )}
            </React.Fragment>
          ))}
        </div>
      )}

      <Dialog open={dialogOpen} onOpenChange={(_e, data) => setDialogOpen(data.open)}>
        <DialogSurface>
          <DialogTitle>Ajouter un utilisateur</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              <Field label="Prénom" required>
                <Input
                  value={firstName}
                  onChange={(_e, data) => setFirstName(data.value)}
                  disabled={submitting}
                />
              </Field>
              <Field label="Nom" required>
                <Input
                  value={lastName}
                  onChange={(_e, data) => setLastName(data.value)}
                  disabled={submitting}
                />
              </Field>
              <Field label="Email" required>
                <Input
                  type="email"
                  value={email}
                  onChange={(_e, data) => setEmail(data.value)}
                  disabled={submitting}
                />
              </Field>
              <Field label="Téléphone" required>
                <Input
                  type="tel"
                  value={phoneNumber}
                  onChange={(_e, data) => setPhoneNumber(data.value)}
                  disabled={submitting}
                />
              </Field>
              <Field label="Rôle" required>
                <Dropdown
                  value={ROLE_LABELS[role]}
                  selectedOptions={[role]}
                  disabled={submitting}
                  onOptionSelect={(_e, data) => data.optionValue && setRole(data.optionValue as DiagLinkRole)}
                >
                  {ASSIGNABLE_ROLES.map(r => (
                    <Option key={r} value={r}>
                      {ROLE_LABELS[r]}
                    </Option>
                  ))}
                </Dropdown>
              </Field>
              {formError && <Text className={styles.formError}>{formError}</Text>}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setDialogOpen(false)} disabled={submitting}>
                Annuler
              </Button>
              <Button appearance="primary" onClick={handleCreateTechnician} disabled={submitting}>
                {submitting ? <Spinner size="tiny" /> : "Ajouter l'utilisateur"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
      <Dialog open={machineDialogOpen} onOpenChange={(_event, data) => !savingAccess && setMachineDialogOpen(data.open)}>
        <DialogSurface>
          <DialogTitle>Machines autorisées — {selectedUser?.email}</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              {machinesLoading && <ViewMessage loading message="Chargement des machines..." />}
              {machinesError && <Text className={styles.formError}>{machinesError}</Text>}
              {!machinesLoading && machines && machines.length === 0 && <Text>Aucune machine disponible pour votre entreprise.</Text>}
              {!machinesLoading && machines && machines.length > 0 && (
                <div className={styles.machineList}>
                  {machines.map(machine => (
                    <Checkbox
                      key={machine.machineId}
                      checked={checkedMachineIds.has(machine.machineId)}
                      onChange={() => toggleMachine(machine.machineId)}
                      disabled={savingAccess}
                      label={<div className={styles.machineLabel}><Text>{machine.name}</Text>{machine.reference && <Text size={200} className={styles.machineSecondary}>{machine.reference}</Text>}</div>}
                    />
                  ))}
                  {accessConfirmation && <Text>{accessConfirmation}</Text>}
                </div>
              )}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={savingAccess} onClick={() => setMachineDialogOpen(false)}>Annuler</Button>
              <Button appearance="primary" disabled={savingAccess || machinesLoading || !machines} onClick={handleSaveAccess}>
                {savingAccess ? <Spinner size="tiny" /> : 'Enregistrer'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
      <Dialog open={!!deleteTarget} onOpenChange={(_event, data) => !deleting && !data.open && setDeleteTarget(null)}>
        <DialogSurface>
          <DialogTitle>Désactiver l'accès ?</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              <Text weight="semibold">{[deleteTarget?.firstName, deleteTarget?.lastName].filter(Boolean).join(' ') || deleteTarget?.email}</Text>
              <Text>{deleteTarget?.email}</Text>
              <Text className={styles.machineSecondary}>Cette action supprimera l'accès de cet utilisateur à DiagLink.</Text>
              {deleteError && <Text className={styles.formError}>{deleteError}</Text>}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={deleting} onClick={() => setDeleteTarget(null)}>
                Annuler
              </Button>
              <Button appearance="primary" className={styles.dangerButton} disabled={deleting} onClick={handleConfirmDelete}>
                {deleting ? <Spinner size="tiny" /> : 'Désactiver'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
      <Dialog open={!!permanentDeleteTarget} onOpenChange={(_event, data) => !permanentlyDeleting && !data.open && setPermanentDeleteTarget(null)}>
        <DialogSurface>
          <DialogTitle>Supprimer définitivement cet utilisateur ?</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              <Text weight="semibold">{[permanentDeleteTarget?.firstName, permanentDeleteTarget?.lastName].filter(Boolean).join(' ') || permanentDeleteTarget?.email}</Text>
              <Text>{permanentDeleteTarget?.email}</Text>
              <Text className={styles.machineSecondary}>Cette action est définitive. L'utilisateur sera supprimé de DiagLink et ne pourra pas être restauré.</Text>
              {permanentDeleteError && <Text className={styles.formError}>{permanentDeleteError}</Text>}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={permanentlyDeleting} onClick={() => setPermanentDeleteTarget(null)}>
                Annuler
              </Button>
              <Button appearance="primary" className={styles.dangerButton} disabled={permanentlyDeleting} onClick={handleConfirmPermanentDelete}>
                {permanentlyDeleting ? <Spinner size="tiny" /> : 'Supprimer définitivement'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </ViewRoot>
  );
};

