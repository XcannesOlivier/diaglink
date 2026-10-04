import React, { useCallback, useEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Text, Badge, Button, Dialog, DialogSurface, DialogTitle, DialogBody, DialogContent, DialogActions, Field, Input, Spinner } from '@fluentui/react-components';
import { ArrowLeftRegular } from '@fluentui/react-icons';
import { ViewRoot, ViewMessage } from './ViewLayout';
import { useApiResource } from '../../hooks/useApiResource';
import { getCompanies } from '../../services/companyService';
import { addMachineWithDocuments, getMachines } from '../../services/machineService';
import { isSuperAdmin, isCompanyAdmin } from '../../utils/roles';
import { useAppContext } from '../../contexts/AppContext';
import type { CompanyDto } from '../../types/company';
import type { CurrentUser } from '../../types/currentUser';
import type { MachineDto } from '../../types/machine';
import { MachineCreditStatus } from './MachineCreditStatus';
import { AdditionalMachineRequestDialog } from './AdditionalMachineRequestDialog';
import { AdditionalDocumentsRequestDialog } from './AdditionalDocumentsRequestDialog';
import { DialogCloseButton } from '../core/DialogCloseButton';

const useStyles = makeStyles({
  layout: {
    display: 'flex',
    gap: tokens.spacingHorizontalL,
    alignItems: 'flex-start',
    '@media (max-width: 768px)': {
      flexDirection: 'column',
    },
  },
  list: {
    flex: 1,
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    width: '100%',
    overflowX: 'auto',
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '3fr 1fr',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    alignItems: 'center',
    cursor: 'pointer',
    '@media (max-width: 768px)': {
      gridTemplateColumns: 'minmax(0, 1fr) auto',
      minWidth: 0,
    },
  },
  rowSelected: {
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  rowUnavailable: {
    cursor: 'not-allowed',
    opacity: 0.6,
    backgroundColor: tokens.colorNeutralBackground3,
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
      gridTemplateColumns: 'minmax(0, 1fr) auto auto',
      minWidth: 0,
      gap: tokens.spacingHorizontalS,
      padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    },
  },
  companyStatus: {
    justifySelf: 'start',
  },
  companyMachineCount: {
    justifySelf: 'start',
  },
  headerRow: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    cursor: 'default',
  },
  companyHeaderRow: {
    cursor: 'default',
    ':hover': {
      backgroundColor: 'transparent',
    },
  },
  machineListContainer: {
    width: '100%',
    maxWidth: '1200px',
    marginLeft: 'auto',
    marginRight: 'auto',
  },
  machineRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(300px, 1fr) 160px',
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
      gridTemplateColumns: 'minmax(0, 1fr) auto',
      minWidth: 0,
      gap: tokens.spacingHorizontalS,
      padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    },
  },
  machineStatus: {
    justifySelf: 'start',
  },
  machineHeaderRow: {
    cursor: 'default',
    ':hover': {
      backgroundColor: 'transparent',
    },
  },
  detail: {
    width: '320px',
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    boxSizing: 'border-box',
    '@media (max-width: 768px)': {
      width: '100%',
    },
  },
  detailRow: {
    display: 'flex',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
  },
  detailLabel: {
    color: tokens.colorNeutralForeground3,
  },
  backButton: {
    marginBottom: tokens.spacingVerticalM,
  },
  companyActions: {
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
  fileList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
});

interface MachinesViewProps {
  currentUser: CurrentUser | null;
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}

export const MachinesView: React.FC<MachinesViewProps> = ({ currentUser, getAccessToken, onDiagLinkSessionExpired }) => {
  const styles = useStyles();
  const { dispatch } = useAppContext();
  const [selectedMachine, setSelectedMachine] = useState<MachineDto | null>(null);
  const machineDetailRef = useRef<HTMLDivElement | null>(null);
  const [selectedCompany, setSelectedCompany] = useState<CompanyDto | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [machineName, setMachineName] = useState('');
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [submittingMachine, setSubmittingMachine] = useState(false);
  const [machineFormError, setMachineFormError] = useState<string | null>(null);
  const [machineConfirmation, setMachineConfirmation] = useState<string | null>(null);
  const [documentsDialogOpen, setDocumentsDialogOpen] = useState(false);
  const [documentFiles, setDocumentFiles] = useState<File[]>([]);
  const [submittingDocuments, setSubmittingDocuments] = useState(false);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [documentsConfirmation, setDocumentsConfirmation] = useState<string | null>(null);
  const [requestDialogOpen, setRequestDialogOpen] = useState(false);
  const [documentsRequestDialogOpen, setDocumentsRequestDialogOpen] = useState(false);
  const superAdmin = isSuperAdmin(currentUser);
  const companyAdmin = isCompanyAdmin(currentUser);
  const fetchMachines = useCallback(() => getMachines(getAccessToken), [getAccessToken, refreshKey]);
  const state = useApiResource(fetchMachines, onDiagLinkSessionExpired);
  const fetchCompanies = useCallback(() => getCompanies(getAccessToken), [getAccessToken]);
  const companiesState = useApiResource(fetchCompanies, onDiagLinkSessionExpired, superAdmin);

  useEffect(() => {
    if (!selectedMachine || documentsRequestDialogOpen || documentsDialogOpen) return;

    const handleClickOutside = (event: MouseEvent | TouchEvent) => {
      if (
        machineDetailRef.current &&
        !machineDetailRef.current.contains(event.target as Node)
      ) {
        setSelectedMachine(null);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('touchstart', handleClickOutside);

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('touchstart', handleClickOutside);
    };
  }, [selectedMachine, documentsRequestDialogOpen, documentsDialogOpen]);

  const handleUseMachine = useCallback((machine: MachineDto) => {
    dispatch({
      type: 'MACHINE_SELECT',
      machine: {
        id: machine.id,
        name: machine.name,
      },
    });
    // Start a fresh conversation scoped to the newly selected machine.
    dispatch({ type: 'CHAT_CLEAR' });
    dispatch({ type: 'UI_SET_VIEW', view: 'chat' });
  }, [dispatch]);

  const subtitle = superAdmin
    ? 'Toutes les machines DiagLink.'
    : isCompanyAdmin(currentUser)
      ? 'Machines de votre entreprise.'
      : 'Machines qui vous sont assignées.';

  const visibleMachines = state.kind === 'success' && selectedCompany
    ? state.data.filter(machine => machine.companyId === selectedCompany.id)
    : state.kind === 'success'
      ? state.data
      : [];

  const isCompanyListView = superAdmin && !selectedCompany;

  const resetMachineForm = () => {
    setMachineName('');
    setSelectedFiles([]);
    setMachineFormError(null);
  };

  const openMachineDialog = () => {
    resetMachineForm();
    setDialogOpen(true);
  };

  const handleAddMachine = async () => {
    if (!selectedCompany || submittingMachine) return;

    const trimmedMachineName = machineName.trim();
    if (!trimmedMachineName || selectedFiles.length === 0) {
      setMachineFormError('Le nom de la machine et au moins un fichier PDF sont obligatoires.');
      return;
    }

    setSubmittingMachine(true);
    setMachineFormError(null);
    const result = await addMachineWithDocuments(getAccessToken, selectedCompany.name, trimmedMachineName, selectedFiles);
    setSubmittingMachine(false);

    if (result.kind === 'success') {
      setDialogOpen(false);
      resetMachineForm();
      setMachineConfirmation('Machine ajoutée.');
      setRefreshKey(key => key + 1);
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setMachineFormError(result.message);
      return;
    }

    if (result.kind === 'error' && 'message' in result) {
      setMachineFormError(result.message);
      return;
    }

    setMachineFormError("Impossible d'ajouter la machine.");
  };

  const openDocumentsDialog = () => {
    setDocumentFiles([]);
    setDocumentsError(null);
    setDocumentsDialogOpen(true);
  };

  const handleAddDocuments = async () => {
    if (submittingDocuments) return;

    if (!selectedCompany) {
      setDocumentsError("Impossible d’ajouter les documents : aucune société sélectionnée.");
      return;
    }

    if (!selectedMachine) {
      setDocumentsError("Impossible d’ajouter les documents : aucune machine sélectionnée.");
      return;
    }

    if (documentFiles.length === 0) {
      setDocumentsError('Au moins un fichier PDF est obligatoire.');
      return;
    }

    setSubmittingDocuments(true);
    setDocumentsError(null);
    const result = await addMachineWithDocuments(getAccessToken, selectedCompany.name, selectedMachine.name, documentFiles);
    setSubmittingDocuments(false);

    if (result.kind === 'success') {
      setDocumentsDialogOpen(false);
      setDocumentFiles([]);
      setDocumentsError(null);
      setDocumentsConfirmation('Documents ajoutés.');
      return;
    }

    if (result.kind === 'unauthorized' && result.diagLinkSessionExpired) {
      onDiagLinkSessionExpired?.();
    }

    if (result.kind === 'validation-error' || result.kind === 'conflict') {
      setDocumentsError(result.message);
      return;
    }

    if (result.kind === 'error' && 'message' in result) {
      setDocumentsError(result.message);
      return;
    }

    setDocumentsError("Impossible d'ajouter les documents.");
  };

  return (
    <ViewRoot title={selectedCompany ? `Machines — ${selectedCompany.name}` : 'Machines'} subtitle={subtitle}>
      {isCompanyListView && companiesState.kind === 'loading' && <ViewMessage loading message="Chargement des entreprises..." />}
      {isCompanyListView && companiesState.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {isCompanyListView && companiesState.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {isCompanyListView && companiesState.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {isCompanyListView && companiesState.kind === 'success' && state.kind === 'loading' && <ViewMessage loading message="Chargement des machines..." />}
      {isCompanyListView && companiesState.kind === 'success' && state.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {isCompanyListView && companiesState.kind === 'success' && state.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {isCompanyListView && companiesState.kind === 'success' && state.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {isCompanyListView && companiesState.kind === 'success' && state.kind === 'success' && (
        <div className={styles.companyListContainer}>
          <div className={styles.list}>
            <div className={`${styles.companyRow} ${styles.headerRow} ${styles.companyHeaderRow}`}>
              <Text>Entreprise</Text>
              <Text>Machines</Text>
              <Text>Statut</Text>
            </div>
            {companiesState.data.map(company => {
              const machineCount = state.data.filter(machine => machine.companyId === company.id).length;
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
                  <Text className={styles.companyMachineCount}>{machineCount}</Text>
                  <Badge appearance="tint" className={styles.companyStatus}>{company.status}</Badge>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {!isCompanyListView && state.kind === 'loading' && <ViewMessage loading message="Chargement des machines..." />}
      {!isCompanyListView && state.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {!isCompanyListView && state.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {!isCompanyListView && state.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {!isCompanyListView && state.kind === 'success' && (
        <div className={selectedCompany ? styles.machineListContainer : undefined}>
          {companyAdmin && <div className={styles.companyActions}>
            <Button appearance="primary" onClick={() => setRequestDialogOpen(true)}>Demander l’ajout d’une machine</Button>
          </div>}
          {selectedCompany && (
            <>
              <div className={styles.companyActions}>
                <Button
                  appearance="secondary"
                  className={styles.backButton}
                  icon={<ArrowLeftRegular />}
                  onClick={() => setSelectedCompany(null)}
                >
                  Retour aux entreprises
                </Button>
                <Button appearance="primary" className={styles.backButton} onClick={openMachineDialog}>
                  Ajouter une machine
                </Button>
              </div>
              {machineConfirmation && <Text>{machineConfirmation}</Text>}
            </>
          )}
          {visibleMachines.length === 0 && <ViewMessage message="Aucune machine disponible." />}
          {visibleMachines.length > 0 && (
            <div className={styles.layout}>
              <div className={styles.list}>
                <div className={`${selectedCompany ? styles.machineRow : styles.row} ${styles.headerRow} ${selectedCompany ? styles.machineHeaderRow : ''}`}>
                  <Text>Nom</Text>
                  <Text>Statut</Text>
                </div>
                {visibleMachines.map(machine => (
                  <div
                    key={machine.id}
                    role="button"
                    tabIndex={0}
                    aria-disabled={!machine.isAccessible}
                    className={`${selectedCompany ? styles.machineRow : styles.row} ${!machine.isAccessible ? styles.rowUnavailable : ''} ${selectedMachine?.id === machine.id ? styles.rowSelected : ''}`}
                    onClick={() => machine.isAccessible && setSelectedMachine(machine)}
                    onKeyDown={event => {
                      if (event.key === 'Enter' && machine.isAccessible) setSelectedMachine(machine);
                    }}
                  >
                    <Text>{machine.name}</Text>
                    <Badge appearance="tint" className={selectedCompany ? styles.machineStatus : undefined}>
                      {machine.isAccessible ? machine.status : 'Indisponible'}
                    </Badge>
                  </div>
                ))}
              </div>

              {selectedMachine && (
                <div ref={machineDetailRef} className={styles.detail}>
                  <Text weight="semibold">{selectedMachine.name}</Text>
                  <MachineCreditStatus machineId={selectedMachine.id} getAccessToken={getAccessToken} />
                  <div className={styles.detailRow}>
                    <Text className={styles.detailLabel}>Statut</Text>
                    <Text>{selectedMachine.status}</Text>
                  </div>
                  {/* Section 10: launches the agent chat scoped to this machine, gated on the machine
                      having a configured assistant (never lets the user start a chat that would 409). */}
                  <Button
                    disabled={!selectedMachine.hasAssistantConfigured}
                    title={selectedMachine.hasAssistantConfigured ? undefined : "Aucun assistant n'est encore configuré pour cette machine."}
                    onClick={() => handleUseMachine(selectedMachine)}
                  >
                    Utiliser cette machine
                  </Button>
                  {superAdmin && selectedCompany && (
                    <Button appearance="secondary" onClick={openDocumentsDialog}>
                      Ajouter des PDF
                    </Button>
                  )}
                  {companyAdmin && (
                    <Button appearance="secondary" onClick={() => setDocumentsRequestDialogOpen(true)}>
                      Demander l’ajout de documents
                    </Button>
                  )}
                  {documentsConfirmation && <Text>{documentsConfirmation}</Text>}
                </div>
              )}
            </div>
          )}
        </div>
      )}
      <Dialog open={dialogOpen} onOpenChange={(_event, data) => !submittingMachine && setDialogOpen(data.open)}>
        <DialogSurface>
          <DialogTitle action={<DialogCloseButton disabled={submittingMachine} onClick={() => setDialogOpen(false)} />}>Ajouter une machine</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              <Field label="Nom de la machine" required>
                <Input
                  value={machineName}
                  onChange={(_event, data) => setMachineName(data.value)}
                  disabled={submittingMachine}
                />
              </Field>
              <Field label="Fichiers PDF" required>
                <input
                  type="file"
                  accept=".pdf,application/pdf"
                  multiple
                  disabled={submittingMachine}
                  onChange={event => setSelectedFiles(Array.from(event.target.files ?? []))}
                />
              </Field>
              {selectedFiles.length > 0 && (
                <div className={styles.fileList}>
                  {selectedFiles.map((file, index) => <Text key={`${file.name}-${index}`}>{file.name}</Text>)}
                </div>
              )}
              {machineFormError && <Text className={styles.formError}>{machineFormError}</Text>}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={submittingMachine} onClick={() => setDialogOpen(false)}>
                Annuler
              </Button>
              <Button appearance="primary" disabled={submittingMachine} onClick={handleAddMachine}>
                {submittingMachine ? <Spinner size="tiny" /> : 'Ajouter la machine'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
      {companyAdmin && <AdditionalMachineRequestDialog open={requestDialogOpen} onOpenChange={setRequestDialogOpen} getAccessToken={getAccessToken} />}
      {companyAdmin && selectedMachine && <AdditionalDocumentsRequestDialog open={documentsRequestDialogOpen}
        onOpenChange={setDocumentsRequestDialogOpen} machine={selectedMachine} getAccessToken={getAccessToken} />}
      <Dialog open={documentsDialogOpen} onOpenChange={(_event, data) => !submittingDocuments && setDocumentsDialogOpen(data.open)}>
        <DialogSurface>
          <DialogTitle action={<DialogCloseButton disabled={submittingDocuments} onClick={() => setDocumentsDialogOpen(false)} />}>Ajouter des PDF à {selectedMachine?.name}</DialogTitle>
          <DialogBody>
            <DialogContent className={styles.form}>
              <Field label="Fichiers PDF" required>
                <input
                  type="file"
                  accept=".pdf,application/pdf"
                  multiple
                  disabled={submittingDocuments}
                  onChange={event => setDocumentFiles(Array.from(event.target.files ?? []))}
                />
              </Field>
              {documentFiles.length > 0 && (
                <div className={styles.fileList}>
                  {documentFiles.map((file, index) => <Text key={`${file.name}-${index}`}>{file.name}</Text>)}
                </div>
              )}
              {documentsError && <Text className={styles.formError}>{documentsError}</Text>}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" disabled={submittingDocuments} onClick={() => setDocumentsDialogOpen(false)}>
                Annuler
              </Button>
              <Button appearance="primary" disabled={submittingDocuments} onClick={handleAddDocuments}>
                {submittingDocuments ? <Spinner size="tiny" /> : 'Ajouter les PDF'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </ViewRoot>
  );
};
