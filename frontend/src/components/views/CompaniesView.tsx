import React, { useCallback, useState } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Field,
  Input,
  Spinner,
} from '@fluentui/react-components';
import { ViewRoot, ViewMessage } from './ViewLayout';
import { useApiResource } from '../../hooks/useApiResource';
import { getCompanies, onboardCompany } from '../../services/companyService';
import { StripeCompanyPanel } from './StripeCompanyPanel';
import { getApiAuthHeaders } from '../../utils/apiAuth';
import type { CompanyUserDto } from '../../types/company';
import type { CompanyDto } from '../../types/company';
import { DialogCloseButton } from '../core/DialogCloseButton';

const useStyles = makeStyles({
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  row: {
    display: 'flex',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    '@media (min-width: 1024px)': {
      display: 'grid',
      gridTemplateColumns: 'minmax(140px, 1fr) minmax(150px, 1fr) minmax(200px, 1.25fr) minmax(140px, 1fr) auto',
      alignItems: 'center',
      width: '100%',
      boxSizing: 'border-box',
    },
  },
  listHeader: {
    display: 'none',
    '@media (min-width: 1024px)': {
      display: 'grid',
      gridTemplateColumns: 'minmax(140px, 1fr) minmax(150px, 1fr) minmax(200px, 1.25fr) minmax(140px, 1fr) auto',
      gap: tokens.spacingHorizontalM,
      padding: `0 ${tokens.spacingHorizontalM}`,
      color: tokens.colorNeutralForeground2,
    },
  },
  desktopOnly: {
    display: 'none',
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    '@media (min-width: 1024px)': {
      display: 'block',
    },
  },
  status: {
    textAlign: 'right',
  },
  mobileCompanyInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    minWidth: 0,
    '@media (min-width: 1024px)': {
      display: 'none',
    },
  },
  rowSelected: {
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: tokens.spacingVerticalM,
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  dialogContentScroll: {
    maxHeight: '70vh',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  dialogSurface: {
    width: 'min(900px, 95vw)',
    margin: '8vh auto',
    padding: tokens.spacingVerticalM,
    '@media (max-width: 1024px)': {
      width: 'min(760px, 95vw)',
      padding: tokens.spacingVerticalS,
      margin: '6vh auto',
    },
    '@media (max-width: 600px)': {
      width: '95vw',
      padding: tokens.spacingVerticalS,
      margin: '4vh 8px',
    },
  },
  fileList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    maxHeight: '20vh',
    overflowY: 'auto',
  },
  formError: {
    color: tokens.colorPaletteRedForeground1,
  },
  detailPanel: {
    display: 'none',
  },
  adminDetails: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
});

interface CompaniesViewProps {
  getAccessToken: () => Promise<string | null>;
  onDiagLinkSessionExpired?: () => void;
}

/** diaglink_super_admin only — GET /api/companies + POST /api/companies/onboard. */
export const CompaniesView: React.FC<CompaniesViewProps> = ({ getAccessToken, onDiagLinkSessionExpired }) => {
  const styles = useStyles();
  const [selectedCompany, setSelectedCompany] = useState<CompanyDto | null>(null);
  const [companyAdmins, setCompanyAdmins] = useState<CompanyUserDto[] | null>(null);
  const [companyAdminsMap, setCompanyAdminsMap] = useState<Record<string, CompanyUserDto[] | null>>({});
  const [loadingAdminsMap, setLoadingAdminsMap] = useState<Record<string, boolean>>({});
  const [loadingAdmins, setLoadingAdmins] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [companyName, setCompanyName] = useState('');
  const [adminEmail, setAdminEmail] = useState('');
  const [managerLastName, setManagerLastName] = useState('');
  const [managerFirstName, setManagerFirstName] = useState('');
  const [managerPhone, setManagerPhone] = useState('');
  const [machineName, setMachineName] = useState('');
  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [confirmation, setConfirmation] = useState<string | null>(null);

  const [refreshKey, setRefreshKey] = useState(0);
  // refreshKey in the deps gives fetchCompanies a new identity after a successful onboarding,
  // which is what triggers useApiResource to refetch (it refetches whenever the fetcher reference changes).
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const fetchCompanies = useCallback(() => getCompanies(getAccessToken), [getAccessToken, refreshKey]);
  const state = useApiResource(fetchCompanies, onDiagLinkSessionExpired);

  const resetForm = () => {
    setCompanyName('');
    setAdminEmail('');
    setManagerLastName('');
    setManagerFirstName('');
    setManagerPhone('');
    setMachineName('');
    setSelectedFiles([]);
    setFormError(null);
  };

  React.useEffect(() => {
    if (!selectedCompany) {
      setCompanyAdmins(null);
      return;
    }

    (async () => {
      setLoadingAdmins(true);
      try {
        const apiUrl = import.meta.env.VITE_API_URL || '/api';
        const { headers } = await getApiAuthHeaders(getAccessToken);
        const resp = await fetch(`${apiUrl}/companies/${encodeURIComponent(selectedCompany.id)}/users`, { headers });
        if (!resp.ok) {
          setCompanyAdmins(null);
          return;
        }
        const data = (await resp.json()) as CompanyUserDto[];
        setCompanyAdmins(data);
      } catch {
        setCompanyAdmins(null);
      } finally {
        setLoadingAdmins(false);
      }
    })();
  }, [selectedCompany, getAccessToken]);

  const fetchAdminsForCompany = useCallback(
    async (companyId: string) => {
      if (companyAdminsMap[companyId] !== undefined) return; // already fetched or attempted
      setLoadingAdminsMap(m => ({ ...m, [companyId]: true }));
      try {
        const apiUrl = import.meta.env.VITE_API_URL || '/api';
        const { headers } = await getApiAuthHeaders(getAccessToken);
        const resp = await fetch(`${apiUrl}/companies/${encodeURIComponent(companyId)}/users`, { headers });
        if (!resp.ok) {
          setCompanyAdminsMap(m => ({ ...m, [companyId]: null }));
          return;
        }
        const data = (await resp.json()) as CompanyUserDto[];
        setCompanyAdminsMap(m => ({ ...m, [companyId]: data }));
      } catch {
        setCompanyAdminsMap(m => ({ ...m, [companyId]: null }));
      } finally {
        setLoadingAdminsMap(m => ({ ...m, [companyId]: false }));
      }
    },
    [getAccessToken, companyAdminsMap]
  );

  // On desktop, prefetch admin lists so we can show admin email inline in the list rows.
  React.useEffect(() => {
    if (state.kind !== 'success') return;
    const isDesktop = typeof window !== 'undefined' && window.innerWidth >= 1024;
    if (!isDesktop) return;
    state.data.forEach(c => {
      void fetchAdminsForCompany(c.id);
    });
  }, [state, fetchAdminsForCompany]);

  const isFormComplete = (() => {
    const tn = companyName.trim();
    const te = adminEmail.trim();
    const tf = managerFirstName.trim();
    const tl = managerLastName.trim();
    const tp = managerPhone.trim();
    const tm = machineName.trim();
    return Boolean(tn && te && tf && tl && tp && tm && selectedFiles.length > 0);
  })();

  const openDialog = () => {
    resetForm();
    setDialogOpen(true);
  };

  const handleSubmit = async () => {
    if (submitting) return;

    const trimmedName = companyName.trim();
    const trimmedEmail = adminEmail.trim();
    const trimmedManagerFirst = managerFirstName.trim();
    const trimmedManagerLast = managerLastName.trim();
    const trimmedManagerPhone = managerPhone.trim();

    if (!trimmedName || !trimmedEmail) {
      setFormError("Le nom de l'entreprise et l'email de l'administrateur sont obligatoires.");
      return;
    }

    setSubmitting(true);
    setFormError(null);

    const result = await onboardCompany(
      getAccessToken,
      trimmedName,
      trimmedEmail,
      trimmedManagerFirst,
      trimmedManagerLast,
      trimmedManagerPhone
    );

    if (result.kind !== 'success') {
      setSubmitting(false);
      if (result.kind === 'validation-error' || result.kind === 'conflict') {
        setFormError(result.message);
        return;
      }

      if (result.kind === 'unauthorized' || result.kind === 'forbidden') {
        setFormError('Accès non autorisé.');
        return;
      }

      setFormError("Impossible de créer l'entreprise.");
      return;
    }

    // Company created — if files selected, upload them to the backend upload endpoint
    try {
      if (selectedFiles.length > 0) {
        const formData = new FormData();
        formData.append('companyName', trimmedName);
        formData.append('machineName', machineName.trim() || 'default');
        selectedFiles.forEach(f => formData.append('files', f));

        const { headers } = await getApiAuthHeaders(getAccessToken);

        const resp = await fetch('/api/files/upload', {
          method: 'POST',
          body: formData,
          headers,
        });

        if (!resp.ok) {
          const txt = await resp.text();
          throw new Error(`Upload failed: ${resp.status} ${txt}`);
        }
      }

      // success
      setDialogOpen(false);
      resetForm();
      setSelectedCompany(result.data.company);
      setConfirmation('Entreprise créée.');
      setRefreshKey(k => k + 1);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setFormError(err.message ?? "Échec de l'upload des fichiers.");
      } else {
        setFormError("Échec de l'upload des fichiers.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ViewRoot title="Entreprises" subtitle="Gestion des entreprises clientes DiagLink.">
      <div className={styles.header}>
        {confirmation && <Text>{confirmation}</Text>}
        <Button appearance="primary" onClick={openDialog} style={{ marginLeft: 'auto' }}>
          Ajouter une entreprise
        </Button>
      </div>

      {state.kind === 'loading' && <ViewMessage loading message="Chargement des entreprises..." />}
      {state.kind === 'unauthorized' && <ViewMessage message="Votre session a expiré. Veuillez vous reconnecter." />}
      {state.kind === 'forbidden' && <ViewMessage message="Accès non autorisé." />}
      {state.kind === 'error' && <ViewMessage message="Impossible de charger les données." />}
      {state.kind === 'success' && state.data.length === 0 && <ViewMessage message="Aucune entreprise cliente." />}
      {state.kind === 'success' && state.data.length > 0 && (
        <div className={styles.list}>
          <div className={styles.listHeader}>
            <Text weight="semibold">Entreprise</Text>
            <Text weight="semibold">Administrateur</Text>
            <Text weight="semibold">Email</Text>
            <Text weight="semibold">Téléphone</Text>
            <Text weight="semibold" className={styles.status}>Statut</Text>
          </div>
          {state.data.map(company => {
            const admins = companyAdminsMap[company.id];
            const loading = !!loadingAdminsMap[company.id];
            // Aucun company_admin pour cette entreprise ? on retombe sur un diaglink_super_admin
            // associé, purement pour l'affichage — ne change ni les rôles ni les autorisations.
            const admin = admins
              ? admins.find(u => u.role === 'company_admin') ?? admins.find(u => u.role === 'diaglink_super_admin')
              : undefined;
            const adminName = admin
              ? [admin.firstName, admin.lastName].filter(Boolean).join(' ') || '—'
              : '—';
            const adminEmailInline = admin ? admin.email : null;
            const adminPhone = admin?.phoneNumber ?? '—';

            return (
              <React.Fragment key={company.id}>
                <div
                  role="button"
                  tabIndex={0}
                  className={`${styles.row} ${selectedCompany?.id === company.id ? styles.rowSelected : ''}`}
                  onClick={() => setSelectedCompany(company)}
                  onKeyDown={event => {
                    if (event.key === 'Enter') setSelectedCompany(company);
                  }}
                >
                  <div className={styles.mobileCompanyInfo}>
                    <Text weight="semibold">{company.name}</Text>
                  </div>
                  <Text weight="semibold" className={styles.desktopOnly}>{company.name}</Text>
                  {loading ? <Spinner className={styles.desktopOnly} size="tiny" /> : <Text className={styles.desktopOnly}>{adminName}</Text>}
                  <Text className={styles.desktopOnly}>{adminEmailInline ?? '—'}</Text>
                  <Text className={styles.desktopOnly}>{adminPhone}</Text>
                  <Text className={styles.status}>{company.status}</Text>
                </div>
                {selectedCompany?.id === company.id && (
                  <div className={styles.detailPanel}>
                    <Text weight="semibold">{selectedCompany.name}</Text>
                    <Text>Statut : {selectedCompany.status}</Text>
                    <StripeCompanyPanel key={selectedCompany.id} companyId={selectedCompany.id}
                      getAccessToken={getAccessToken} onDiagLinkSessionExpired={onDiagLinkSessionExpired} />
                    <Text weight="semibold">Administrateurs</Text>
                    {loadingAdmins ? (
                      <Text>Chargement...</Text>
                    ) : companyAdmins && companyAdmins.some(u => u.role === 'company_admin') ? (
                      companyAdmins
                        .filter(u => u.role === 'company_admin')
                        .map(u => (
                          <div key={u.id} className={styles.adminDetails}>
                            <Text weight="semibold">{[u.firstName, u.lastName].filter(Boolean).join(' ') || '—'}</Text>
                            <Text>{u.email ?? '—'}</Text>
                            <Text>{u.phoneNumber ?? '—'}</Text>
                          </div>
                        ))
                    ) : (
                      <Text>Liste non disponible pour le moment.</Text>
                    )}
                  </div>
                )}
              </React.Fragment>
            );
          })}
        </div>
      )}

      <Dialog open={dialogOpen} onOpenChange={(_e, data) => setDialogOpen(data.open)}>
        <DialogSurface className={styles.dialogSurface}>
            <DialogTitle action={<DialogCloseButton disabled={submitting} onClick={() => setDialogOpen(false)} />}>Ajouter une entreprise</DialogTitle>
            <DialogBody>
              <DialogContent className={styles.dialogContentScroll}>
                <div className={styles.form}>
                  <Field label="Nom de l'entreprise" required>
                    <Input
                      value={companyName}
                      onChange={(_e, data) => setCompanyName(data.value)}
                      disabled={submitting}
                    />
                  </Field>
                  <Field label="Email de l'administrateur" required>
                    <Input
                      type="email"
                      value={adminEmail}
                      onChange={(_e, data) => setAdminEmail(data.value)}
                      disabled={submitting}
                    />
                  </Field>

                  <Field label="Nom du responsable" required>
                    <Input
                      value={managerLastName}
                      onChange={(_e, data) => setManagerLastName(data.value)}
                      disabled={submitting}
                    />
                  </Field>
                  <Field label="Prénom du responsable" required>
                    <Input
                      value={managerFirstName}
                      onChange={(_e, data) => setManagerFirstName(data.value)}
                      disabled={submitting}
                    />
                  </Field>
                  <Field label="Numéro de téléphone" required>
                    <Input
                      value={managerPhone}
                      onChange={(_e, data) => setManagerPhone(data.value)}
                      disabled={submitting}
                    />
                  </Field>
                  <Field label="Nom de la première machine" required>
                    <Input
                      value={machineName}
                      onChange={(_e, data) => setMachineName(data.value)}
                      disabled={submitting}
                    />
                  </Field>
                  

                  <Field label="Fichiers PDF (plusieurs)" required>
                    <input
                      type="file"
                      accept=".pdf,application/pdf"
                      multiple
                      onChange={e => setSelectedFiles(Array.from(e.target.files || []))}
                      disabled={submitting}
                    />
                    <Text>.pdf uniquement</Text>
                  </Field>

                  {selectedFiles.length > 0 && (
                    <div className={styles.fileList}>
                      {selectedFiles.map((f, i) => (
                        <Text key={`${f.name}-${i}`}>{f.name}</Text>
                      ))}
                    </div>
                  )}

                  {formError && <Text className={styles.formError}>{formError}</Text>}
                </div>
              </DialogContent>
              <DialogActions>
                <Button appearance="secondary" onClick={() => setDialogOpen(false)} disabled={submitting}>
                  Annuler
                </Button>
                <Button appearance="primary" onClick={handleSubmit} disabled={!isFormComplete || submitting}>
                  {submitting ? <Spinner size="tiny" /> : "Créer l'entreprise"}
                </Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
      </Dialog>
    </ViewRoot>
  );
};

