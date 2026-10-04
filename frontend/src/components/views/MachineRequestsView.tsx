import { useCallback, useEffect, useRef, useState, type SyntheticEvent } from "react";
import {
  Badge,
  Button,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Input,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowLeft20Regular,
  ArrowDownload20Regular,
} from "@fluentui/react-icons";
import {
  activateMachineRequest,
  attachMachineRequestBusinessEntities,
  cancelMachineRequestPayment,
  captureMachineRequestPayment,
  configureMachineRequestSubscription,
  decideAdditionalDocumentsRequest,
  decideAdditionalMachineRequest,
  downloadMachineRequestDocument,
  getMachineRequest,
  linkMachineRequestCustomer,
  listArchivedMachineRequests,
  markMachineRequestReady,
  updateMachineRequestArchive,
  updateMachineRequestStatus,
  type MachineRequestDetail,
  type MachineRequestListItem,
  type MachineRequestStatus,
} from "../../services/machineRequestAdminApi";
import { getCompanies } from "../../services/companyService";
import { getMachines } from "../../services/machineService";
import type { CompanyDto } from "../../types/company";
import type { MachineDto } from "../../types/machine";
import { additionalDocumentsMinimumAmountCents, additionalDocumentsPricePerPageCents } from "./additionalDocumentsPricing";
import { DialogCloseButton } from "../core/DialogCloseButton";
import { MachineRequestWorkflowStepper } from "./MachineRequestWorkflowStepper";
import { getMachineRequestWorkflow } from "./machineRequestWorkflow";
import { ViewRoot } from "./ViewLayout";

const requestTableColumns = "120px minmax(0,1fr) minmax(0,1fr) minmax(0,1fr) 90px 110px 110px minmax(150px,.9fr)";

const useStyles = makeStyles({
  list: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  listHeader: {
    display: "none",
    padding: `0 calc(${tokens.spacingHorizontalM} + 1px)`,
    color: tokens.colorNeutralForeground3,
  },
  requestTableGrid: {
    "@media (min-width: 1024px)": {
      display: "grid",
      gridTemplateColumns: requestTableColumns,
      alignItems: "center",
      gap: tokens.spacingHorizontalM,
    },
  },
  requestRow: {
    padding: tokens.spacingVerticalM,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    minWidth: 0,
    maxWidth: "100%",
    boxSizing: "border-box",
    "@media (max-width: 1023px)": {
      display: "grid",
      gridTemplateColumns: "minmax(0,1fr)",
      gap: tokens.spacingVerticalS,
    },
  },
  waitingRow: {
    borderLeft: `4px solid ${tokens.colorPaletteYellowBorderActive}`,
    paddingLeft: `calc(${tokens.spacingHorizontalM} - 3px)`,
  },
  mobileLabel: {
    color: tokens.colorNeutralForeground3,
    marginRight: tokens.spacingHorizontalXS,
    "@media (min-width: 1024px)": { display: "none" },
  },
  cell: {
    minWidth: 0,
    overflowWrap: "anywhere",
    "@media (max-width: 1023px)": {
      display: "block",
      marginBottom: tokens.spacingVerticalS,
    },
  },
  dateCell: {
    "@media (max-width: 1023px)": {
      display: "flex",
      flexDirection: "column",
      gridColumn: "1 / -1",
      gap: tokens.spacingVerticalXS,
      marginBottom: 0,
    },
  },
  requestType: { display: "block" },
  compactOnlyLabel: {
    display: "none",
    "@media (max-width: 1023px)": {
      display: "inline",
      color: tokens.colorNeutralForeground3,
      marginRight: tokens.spacingHorizontalXS,
    },
  },
  companyCell: {
    "@media (max-width: 1023px)": {
      gridColumn: "1 / -1",
      marginBottom: 0,
      maxWidth: "100%",
    },
  },
  desktopOnly: {
    "@media (max-width: 1023px)": { display: "none !important" },
  },
  detailGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(2,minmax(0,1fr))",
    gap: tokens.spacingHorizontalL,
    minWidth: 0,
    "@media (max-width: 800px)": {
      gridTemplateColumns: "minmax(0,1fr)",
      gap: tokens.spacingVerticalM,
    },
  },
  detailCard: {
    padding: tokens.spacingVerticalL,
    gap: tokens.spacingVerticalM,
    minWidth: 0,
    maxWidth: "100%",
    boxSizing: "border-box",
    "@media (max-width: 800px)": {
      padding: tokens.spacingVerticalM,
      gap: tokens.spacingVerticalS,
    },
  },
  fullWidth: {
    gridColumn: "1 / -1",
    "@media (max-width: 800px)": { gridColumn: "auto" },
  },
  cardTitle: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  detailList: {
    margin: 0,
    display: "grid",
    gridTemplateColumns: "minmax(130px,.7fr) minmax(0,1.3fr)",
    gap: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    "@media (max-width: 520px)": {
      gridTemplateColumns: "minmax(88px,40%) minmax(0,1fr)",
      gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    },
  },
  term: { color: tokens.colorNeutralForeground3, minWidth: 0, overflowWrap: "anywhere" },
  value: { margin: 0, minWidth: 0, overflowWrap: "anywhere" },
  detailHeader: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalS,
    "& > div": { minWidth: 0, overflowWrap: "anywhere" },
    "& > .fui-Button": { flexShrink: 0 },
    "@media (max-width: 1000px)": {
      paddingRight: "52px",
      boxSizing: "border-box",
    },
    "@media (max-width: 520px)": {
      flexDirection: "column",
      alignItems: "stretch",
      gap: tokens.spacingVerticalS,
      "& > .fui-Button": {
        alignSelf: "flex-end",
        maxWidth: "100%",
        whiteSpace: "normal",
      },
    },
  },
  detailBackRow: {
    display: "flex",
    marginBottom: tokens.spacingVerticalL,
    minWidth: 0,
  },
  documents: { margin: 0, padding: 0, listStyle: "none" },
  document: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} 0`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  documentInfo: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
    overflowWrap: "anywhere",
  },
  totals: {
    display: "flex",
    gap: tokens.spacingHorizontalXL,
    marginTop: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
    flexWrap: "wrap",
  },
  priceTotal: {
    paddingTop: tokens.spacingVerticalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    fontWeight: tokens.fontWeightSemibold,
  },
  actions: {
    display: "flex",
    justifyContent: "flex-end",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalXL,
  },
  state: {
    minHeight: "180px",
    display: "grid",
    placeItems: "center",
    textAlign: "center",
    color: tokens.colorNeutralForeground2,
  },
  error: { color: tokens.colorPaletteRedForeground1 },
  provisioningFields: {
    display: "grid",
    gridTemplateColumns: "repeat(2,minmax(0,1fr))",
    gap: tokens.spacingHorizontalL,
    "@media (max-width: 700px)": { gridTemplateColumns: "1fr" },
  },
  provisioningField: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  select: {
    minHeight: "32px",
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: `0 ${tokens.spacingHorizontalS}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  history: {
    marginTop: tokens.spacingVerticalXXL,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingTop: tokens.spacingVerticalL,
  },
  historySummary: {
    cursor: "pointer",
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    scrollMarginTop: "calc(64px + env(safe-area-inset-top, 0px))",
  },
  historyContent: {
    display: "grid",
    gap: tokens.spacingVerticalM,
    marginTop: tokens.spacingVerticalM,
  },
  historySearch: { width: "100%", maxWidth: "520px" },
  rowActions: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    gap: tokens.spacingVerticalXS,
    "@media (max-width: 1023px)": {
      display: "flex",
      flexDirection: "row",
      alignItems: "center",
      justifyContent: "space-between",
      gridColumn: "1 / -1",
      gap: tokens.spacingHorizontalM,
      marginBottom: 0,
    },
  },
  compactAction: {
    "@media (max-width: 1023px)": {
      flexShrink: 0,
      "& .fui-Button": { minHeight: "44px" },
    },
  },
});

const statusLabels: Record<MachineRequestStatus, string> = {
  pending: "En attente",
  treated: "Traitée",
  rejected: "Refusée",
};
const currency = new Intl.NumberFormat("fr-FR", {
  style: "currency",
  currency: "EUR",
});
const date = new Intl.DateTimeFormat("fr-FR", { dateStyle: "long" });

function StatusBadge({ status }: { status: MachineRequestStatus }) {
  return (
    <Badge
      appearance="tint"
      color={
        status === "pending"
          ? "warning"
          : status === "treated"
            ? "success"
            : "danger"
      }
    >
      {statusLabels[status]}
    </Badge>
  );
}

function formatBytes(size: number) {
  return size >= 1024 * 1024
    ? `${(size / (1024 * 1024)).toLocaleString("fr-FR", { maximumFractionDigits: 1 })} Mo`
    : `${Math.max(1, Math.round(size / 1024)).toLocaleString("fr-FR")} Ko`;
}

function requestKindLabel(kind: MachineRequestListItem["requestKind"]) {
  switch (kind) {
    case "initialMachine":
    case undefined:
      return "Première machine";
    case "additionalMachine":
      return "Ajout de machine";
    case "additionalDocuments":
      return "Ajout de documents";
  }
}

function requestActorLabel(kind: MachineRequestDetail["requestKind"]) {
  switch (kind) {
    case "initialMachine":
    case undefined:
      return "Client · Première machine";
    case "additionalMachine":
      return "Administrateur entreprise · Ajout de machine";
    case "additionalDocuments":
      return "Administrateur entreprise · Ajout de documents";
  }
}

const isArchivable = (status: MachineRequestStatus) => status === "treated" || status === "rejected";

interface MachineRequestsViewProps {
  requests: MachineRequestListItem[];
  isLoading: boolean;
  hasLoadingError: boolean;
  getAccessToken: () => Promise<string | null>;
  onRetry: () => Promise<void>;
  onRequestUpdated: (request: MachineRequestDetail) => void;
  onDiagLinkSessionExpired?: () => void;
  listResetKey?: number;
}

export function MachineRequestsView({
  requests,
  isLoading,
  hasLoadingError,
  getAccessToken,
  onRetry,
  onRequestUpdated,
  onDiagLinkSessionExpired,
  listResetKey = 0,
}: MachineRequestsViewProps) {
  const styles = useStyles();
  const [selectedRequest, setSelectedRequest] =
    useState<MachineRequestDetail | null>(null);
  const [isLoadingDetail, setIsLoadingDetail] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);
  const [downloadingDocumentId, setDownloadingDocumentId] = useState<
    string | null
  >(null);
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [machines, setMachines] = useState<MachineDto[]>([]);
  const [selectedCompanyId, setSelectedCompanyId] = useState("");
  const [selectedMachineId, setSelectedMachineId] = useState("");
  const [isLoadingProvisioning, setIsLoadingProvisioning] = useState(false);
  const [archivedRequests, setArchivedRequests] = useState<MachineRequestListItem[]>([]);
  const [historySearch, setHistorySearch] = useState("");
  const [historyError, setHistoryError] = useState(false);
  const historySummaryRef = useRef<HTMLElement>(null);
  const [archiveAction, setArchiveAction] = useState<{ requestId: string; isArchived: boolean } | null>(null);
  const [isUpdatingArchive, setIsUpdatingArchive] = useState(false);
  const [locallyArchivedIds, setLocallyArchivedIds] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    setSelectedRequest(null);
    setDetailError(null);
    setActionError(null);
  }, [listResetKey]);

  const handleUnauthorized = (result: {
    kind: string;
    diagLinkSessionExpired?: boolean;
  }) => {
    if (result.kind === "unauthorized" && result.diagLinkSessionExpired)
      onDiagLinkSessionExpired?.();
  };

  const loadArchivedRequests = useCallback(async () => {
    const result = await listArchivedMachineRequests(getAccessToken);
    if (result.kind === "success") {
      setArchivedRequests([...result.data].sort((left, right) =>
        new Date(right.archivedAtUtc ?? right.createdAt).getTime() - new Date(left.archivedAtUtc ?? left.createdAt).getTime()));
      setHistoryError(false);
    } else {
      handleUnauthorized(result);
      setHistoryError(true);
    }
  }, [getAccessToken, onDiagLinkSessionExpired]);

  useEffect(() => { void loadArchivedRequests(); }, [loadArchivedRequests]);

  const handleHistoryToggle = (event: SyntheticEvent<HTMLDetailsElement>) => {
    if (!event.currentTarget.open) return;
    window.requestAnimationFrame(() => {
      historySummaryRef.current?.scrollIntoView({
        behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
        block: "start",
      });
    });
  };

  const confirmArchiveChange = async () => {
    if (!archiveAction || isUpdatingArchive) return;
    const action = archiveAction;
    setIsUpdatingArchive(true);
    setActionError(null);
    const result = await updateMachineRequestArchive(
      getAccessToken,
      action.requestId,
      action.isArchived,
    );
    if (result.kind === "success") {
      setLocallyArchivedIds(current => {
        const next = new Set(current);
        if (action.isArchived) next.add(result.data.requestId); else next.delete(result.data.requestId);
        return next;
      });
      setSelectedRequest(current => current?.requestId === result.data.requestId ? result.data : current);
      onRequestUpdated(result.data);
      await Promise.all([onRetry(), loadArchivedRequests()]);
      setArchiveAction(null);
    } else {
      handleUnauthorized(result);
      setActionError(action.isArchived
        ? "La demande n’a pas pu être archivée."
        : "La demande n’a pas pu être restaurée.");
    }
    setIsUpdatingArchive(false);
  };

  const normalizedHistorySearch = historySearch.trim().toLocaleLowerCase("fr-FR");
  const filteredArchivedRequests = archivedRequests.filter(request => !normalizedHistorySearch || [
    request.company,
    request.firstName,
    request.lastName,
    `${request.firstName} ${request.lastName}`,
    request.email,
    request.machineName,
    request.manufacturer,
    request.model,
    request.serialNumber,
    request.description,
    requestKindLabel(request.requestKind),
  ].some(value => value?.toLocaleLowerCase("fr-FR").includes(normalizedHistorySearch)));
  const activeRequests = requests.filter(request => !request.isArchived && !locallyArchivedIds.has(request.requestId));

  const archiveDialog = <Dialog open={archiveAction !== null} onOpenChange={(_event, data) => !data.open && !isUpdatingArchive && setArchiveAction(null)}>
    <DialogSurface><DialogBody>
      <DialogTitle action={<DialogCloseButton disabled={isUpdatingArchive} onClick={() => setArchiveAction(null)} />}>{archiveAction?.isArchived ? "Archiver cette demande ?" : "Restaurer cette demande ?"}</DialogTitle>
      <DialogContent>{archiveAction?.isArchived
        ? "Elle sera déplacée dans l’historique et restera entièrement consultable."
        : "Elle quittera l’historique et reviendra dans la liste principale sans changer de statut."}</DialogContent>
      <DialogActions>
        <Button appearance="secondary" disabled={isUpdatingArchive} onClick={() => setArchiveAction(null)}>Annuler</Button>
        <Button appearance="primary" disabled={isUpdatingArchive} onClick={() => void confirmArchiveChange()}>{archiveAction?.isArchived ? "Archiver" : "Restaurer"}</Button>
      </DialogActions>
    </DialogBody></DialogSurface>
  </Dialog>;

  const loadProvisioningReferences = async () => {
    setIsLoadingProvisioning(true);
    const [companyResult, machineResult] = await Promise.all([
      getCompanies(getAccessToken),
      getMachines(getAccessToken),
    ]);
    if (companyResult.kind === "success") setCompanies(companyResult.data);
    if (machineResult.kind === "success") setMachines(machineResult.data);
    if (companyResult.kind !== "success" || machineResult.kind !== "success") {
      handleUnauthorized(
        companyResult.kind !== "success" ? companyResult : machineResult,
      );
      setActionError(
        "Impossible de charger les entreprises et machines disponibles.",
      );
    }
    setIsLoadingProvisioning(false);
  };

  const openRequest = async (requestId: string) => {
    setIsLoadingDetail(true);
    setDetailError(null);
    const result = await getMachineRequest(getAccessToken, requestId);
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      setSelectedCompanyId(result.data.payment?.companyId ?? "");
      setSelectedMachineId(result.data.payment?.machineId ?? "");
      if (
        result.data.payment?.status === "captured" &&
        result.data.payment.provisioningStage
      )
        await loadProvisioningReferences();
    } else {
      handleUnauthorized(result);
      setDetailError(
        result.kind === "not-found"
          ? "Cette demande est introuvable."
          : "Impossible de charger le détail de la demande.",
      );
    }
    setIsLoadingDetail(false);
  };

  const attachBusinessEntities = async () => {
    if (
      !selectedRequest ||
      !selectedCompanyId ||
      !selectedMachineId ||
      isUpdatingStatus ||
      !getMachineRequestWorkflow(selectedRequest).actions.attach.enabled
    )
      return;
    setIsUpdatingStatus(true);
    setActionError(null);
    const result = await attachMachineRequestBusinessEntities(
      getAccessToken,
      selectedRequest.requestId,
      selectedCompanyId,
      selectedMachineId,
    );
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
      if (
        result.data.payment?.status === "captured" &&
        result.data.payment.provisioningStage
      )
        await loadProvisioningReferences();
    } else {
      handleUnauthorized(result);
      setActionError(
        "Le rattachement de l’entreprise et de la machine n’a pas pu être enregistré.",
      );
    }
    setIsUpdatingStatus(false);
  };

  const changeStatus = async (status: "treated" | "rejected") => {
    if (!selectedRequest || isUpdatingStatus) return;
    if (status === "treated" && !getMachineRequestWorkflow(selectedRequest).actions.accept.enabled) return;
    if (
      status === "rejected" &&
      !window.confirm("Confirmer le refus de cette demande ?")
    )
      return;
    setIsUpdatingStatus(true);
    setActionError(null);
    if (selectedRequest.requestKind === "additionalDocuments") {
      const result = await decideAdditionalDocumentsRequest(
        getAccessToken,
        selectedRequest.requestId,
        status === "treated" ? "accept" : "reject",
      );
      if (result.kind === "success") {
        setSelectedRequest(result.data);
        onRequestUpdated(result.data);
      } else {
        handleUnauthorized(result);
        setActionError(
          status === "treated"
            ? "L’encaissement du paiement documentaire n’a pas pu être confirmé. La demande peut être réessayée."
            : "L’annulation de l’autorisation documentaire n’a pas pu être confirmée. La demande reste en attente.",
        );
      }
      setIsUpdatingStatus(false);
      return;
    }
    if (selectedRequest.requestKind === "additionalMachine") {
      const result = await decideAdditionalMachineRequest(
        getAccessToken,
        selectedRequest.requestId,
        status === "treated" ? "accept" : "reject",
      );
      if (result.kind === "success") {
        setSelectedRequest(result.data);
        onRequestUpdated(result.data);
      } else {
        handleUnauthorized(result);
        setActionError(
          status === "treated"
            ? "La capture ou la création de la Machine n’a pas pu être confirmée. La demande peut être réessayée."
            : "L’annulation de l’autorisation n’a pas pu être confirmée. La demande reste en attente.",
        );
      }
      setIsUpdatingStatus(false);
      return;
    }
    if (selectedRequest.payment) {
      const expectedPaymentStatus =
        status === "treated" ? "captured" : "cancelled";
      const paymentResult =
        status === "treated"
          ? await captureMachineRequestPayment(
              getAccessToken,
              selectedRequest.payment.paymentRequestId,
            )
          : await cancelMachineRequestPayment(
              getAccessToken,
              selectedRequest.payment.paymentRequestId,
            );
      if (
        paymentResult.kind !== "success" ||
        paymentResult.data.status !== expectedPaymentStatus
      ) {
        handleUnauthorized(paymentResult);
        setActionError(
          status === "treated"
            ? "L’encaissement Stripe n’a pas pu être confirmé. La demande reste en attente et peut être réessayée."
            : "L’annulation Stripe n’a pas pu être confirmée. La demande reste en attente et peut être réessayée.",
        );
        setIsUpdatingStatus(false);
        return;
      }
    }
    const result = await updateMachineRequestStatus(
      getAccessToken,
      selectedRequest.requestId,
      status,
    );
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
      if (
        result.data.payment?.status === "captured" &&
        result.data.payment.provisioningStage
      )
        await loadProvisioningReferences();
    } else {
      handleUnauthorized(result);
      setActionError(
        "Le statut de la demande n’a pas pu être modifié. Veuillez réessayer.",
      );
    }
    setIsUpdatingStatus(false);
  };

  const downloadDocument = async (documentId: string, originalName: string) => {
    if (!selectedRequest || downloadingDocumentId) return;
    setDownloadingDocumentId(documentId);
    setActionError(null);
    const result = await downloadMachineRequestDocument(
      getAccessToken,
      selectedRequest.requestId,
      documentId,
      originalName,
    );
    if (result.kind !== "success") {
      handleUnauthorized(result);
      setActionError(
        "Le document n’a pas pu être téléchargé. Veuillez réessayer.",
      );
    }
    setDownloadingDocumentId(null);
  };

  const prepareBilling = async () => {
    if (!selectedRequest || isUpdatingStatus || !getMachineRequestWorkflow(selectedRequest).actions.prepareBilling.enabled) return;
    setIsUpdatingStatus(true);
    setActionError(null);
    const result = await linkMachineRequestCustomer(
      getAccessToken,
      selectedRequest.requestId,
    );
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
    } else {
      handleUnauthorized(result);
      setActionError(
        selectedRequest.requestKind === "additionalMachine"
          ? "Le Customer Stripe existant n’a pas pu être vérifié. Vérifiez la configuration de facturation puis réessayez."
          : "Le client Stripe n’a pas pu être rattaché. Vérifiez la configuration de facturation puis réessayez.",
      );
    }
    setIsUpdatingStatus(false);
  };

  const configureSubscription = async () => {
    if (!selectedRequest || isUpdatingStatus || !getMachineRequestWorkflow(selectedRequest).actions.configureSubscription.enabled) return;
    setIsUpdatingStatus(true);
    setActionError(null);
    const result = await configureMachineRequestSubscription(
      getAccessToken,
      selectedRequest.requestId,
    );
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
    } else {
      handleUnauthorized(result);
      setActionError(
        "L’abonnement n’a pas pu être configuré. Vérifiez le Customer, le cycle et la Subscription puis réessayez.",
      );
    }
    setIsUpdatingStatus(false);
  };

  const activateMachine = async () => {
    if (!selectedRequest || isUpdatingStatus || !getMachineRequestWorkflow(selectedRequest).actions.activate.enabled) return;
    setIsUpdatingStatus(true);
    setActionError(null);
    const result = await activateMachineRequest(
      getAccessToken,
      selectedRequest.requestId,
    );
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
    } else {
      handleUnauthorized(result);
      setActionError(
        "L’activation de la machine n’a pas pu être finalisée. Vérifiez l’abonnement puis réessayez.",
      );
    }
    setIsUpdatingStatus(false);
  };

  const markReady = async () => {
    if (!selectedRequest || isUpdatingStatus || !getMachineRequestWorkflow(selectedRequest).actions.markReady.enabled) return;
    const documents = selectedRequest.requestKind === "additionalDocuments";
    const confirmed = window.confirm(documents
      ? "Confirmer que ces documents sont intégrés ?\nLe client recevra un email lui indiquant qu’ils sont disponibles dans DiagLink."
      : "Confirmer que cette machine est prête ?\nLe client recevra un email lui indiquant qu’elle est disponible dans DiagLink.");
    if (!confirmed) return;
    setIsUpdatingStatus(true);
    setActionError(null);
    const result = await markMachineRequestReady(getAccessToken, selectedRequest.requestId);
    if (result.kind === "success") {
      setSelectedRequest(result.data);
      onRequestUpdated(result.data);
    } else {
      handleUnauthorized(result);
      setActionError("L’état prêt n’a pas pu être enregistré. Veuillez réessayer.");
    }
    setIsUpdatingStatus(false);
  };

  if (selectedRequest) {
    const request = selectedRequest;
    const requestKind = request.requestKind ?? "initialMachine";
    const workflow = getMachineRequestWorkflow(request);
    const actionHint = (action: keyof typeof workflow.actions) => !workflow.actions[action].enabled && (
      <Text size={200}>{workflow.actions[action].unavailableReason}</Text>
    );
    return (
      <ViewRoot
        title="Détail de la demande"
        subtitle={`${request.client.company} · ${request.machine.machineName}`}
        headerClassName={styles.detailHeader}
        headerAction={
          request.isArchived ? (
            <Button
              appearance="secondary"
              disabled={isUpdatingArchive}
              onClick={() => setArchiveAction({ requestId: request.requestId, isArchived: false })}
            >
              Restaurer la demande
            </Button>
          ) : isArchivable(request.status) ? (
            <Button
              appearance="subtle"
              disabled={isUpdatingArchive}
              onClick={() => setArchiveAction({ requestId: request.requestId, isArchived: true })}
            >
              Archiver la demande
            </Button>
          ) : undefined
        }
      >
        <div className={styles.detailBackRow}>
          <Button
            appearance="subtle"
            icon={<ArrowLeft20Regular />}
            onClick={() => {
              setSelectedRequest(null);
              setActionError(null);
            }}
          >
            Retour aux demandes
          </Button>
        </div>
        <MachineRequestWorkflowStepper workflow={workflow} />
        <div className={styles.detailGrid}>
          <Card className={styles.detailCard}>
            <Text className={styles.cardTitle}>
              {requestActorLabel(request.requestKind)}
            </Text>
            <dl className={styles.detailList}>
              <dt className={styles.term}>Prénom</dt>
              <dd className={styles.value}>{request.client.firstName}</dd>
              <dt className={styles.term}>Nom</dt>
              <dd className={styles.value}>{request.client.lastName}</dd>
              <dt className={styles.term}>Entreprise</dt>
              <dd className={styles.value}>{request.client.company}</dd>
              <dt className={styles.term}>Adresse e-mail</dt>
              <dd className={styles.value}>{request.client.email}</dd>
              <dt className={styles.term}>Téléphone</dt>
              <dd className={styles.value}>{request.client.phone}</dd>
              <dt className={styles.term}>Statut</dt>
              <dd className={styles.value}>
                <StatusBadge status={request.status} />
              </dd>
            </dl>
          </Card>
          <Card className={styles.detailCard}>
            <Text className={styles.cardTitle}>Machine</Text>
            <dl className={styles.detailList}>
              <dt className={styles.term}>Nom de la machine</dt>
              <dd className={styles.value}>{request.machine.machineName}</dd>
              {request.requestKind !== "additionalDocuments" && <>
                <dt className={styles.term}>Fabricant / marque</dt><dd className={styles.value}>{request.machine.manufacturer}</dd>
                <dt className={styles.term}>Modèle</dt><dd className={styles.value}>{request.machine.model}</dd>
                <dt className={styles.term}>Référence / numéro de série</dt><dd className={styles.value}>{request.machine.serialNumber || "Non renseignée"}</dd>
                <dt className={styles.term}>Description</dt><dd className={styles.value}>{request.machine.description || "Non renseignée"}</dd>
              </>}
            </dl>
          </Card>
          <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
            <Text className={styles.cardTitle}>Documentation</Text>
            <ul className={styles.documents}>
              {request.documents.map((document) => (
                <li className={styles.document} key={document.documentId}>
                  <span className={styles.documentInfo}>
                    <Text weight="semibold">{document.originalName}</Text>
                    <Text size={200}>
                      {document.pageCount} pages · {formatBytes(document.size)}
                    </Text>
                  </span>
                  <Button
                    appearance="subtle"
                    icon={<ArrowDownload20Regular />}
                    disabled={downloadingDocumentId !== null}
                    onClick={() =>
                      void downloadDocument(
                        document.documentId,
                        document.originalName,
                      )
                    }
                  >
                    {downloadingDocumentId === document.documentId
                      ? "Téléchargement…"
                      : "Télécharger"}
                  </Button>
                </li>
              ))}
            </ul>
            <div className={styles.totals}>
              <Text>
                Nombre total de documents :{" "}
                <strong>{request.documents.length}</strong>
              </Text>
              <Text>
                Nombre total de pages :{" "}
                <strong>{request.pricing.totalPages}</strong>
              </Text>
            </div>
          </Card>
          <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
            <Text className={styles.cardTitle}>Tarification</Text>
            {request.requestKind === "additionalDocuments" ? (
              <dl className={styles.detailList}>
                <dt className={styles.term}>Tarif documentaire</dt>
                <dd className={styles.value}>{currency.format(0.27)} HT / page</dd>
                <dt className={styles.term}>Calcul</dt>
                <dd className={styles.value}>
                  {request.pricing.totalPages} × {currency.format(0.27)} HT
                </dd>
                {Math.round(request.pricing.preparationTotal * 100) > request.pricing.totalPages * additionalDocumentsPricePerPageCents && <>
                  <dt className={styles.term}>Minimum par demande</dt>
                  <dd className={styles.value}>{currency.format(additionalDocumentsMinimumAmountCents / 100)} HT</dd>
                </>}
                <dt className={styles.term}>Total</dt>
                <dd className={`${styles.value} ${styles.priceTotal}`}>
                  {currency.format(request.pricing.preparationTotal)} HT
                </dd>
              </dl>
            ) : (
              <dl className={styles.detailList}>
              <dt className={styles.term}>Abonnement DiagLink</dt>
              <dd className={styles.value}>
                {currency.format(request.pricing.monthlySubscriptionPrice)} HT /
                mois / machine
              </dd>
              <dt className={styles.term}>Forfait préparation</dt>
              <dd className={styles.value}>
                {currency.format(request.pricing.basePreparationPrice)} HT
                <br />
                <Text size={200}>
                  Jusqu’à {request.pricing.includedPages} pages
                </Text>
              </dd>
              <dt className={styles.term}>Pages supplémentaires</dt>
              <dd className={styles.value}>
                {request.pricing.additionalPages > 0
                  ? `${request.pricing.additionalPages} × ${currency.format(request.pricing.additionalPagePrice)} HT`
                  : `Aucune · ${currency.format(request.pricing.additionalPagePrice)} HT / page au-delà de ${request.pricing.includedPages} pages`}
              </dd>
              <dt className={styles.term}>Total préparation documentaire</dt>
              <dd className={`${styles.value} ${styles.priceTotal}`}>
                {currency.format(request.pricing.preparationTotal)} HT
              </dd>
              </dl>
            )}
          </Card>
          {request.payment && (
            <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
              <Text className={styles.cardTitle}>Paiement</Text>
              <Text>
                {request.payment.status === "captured"
                  ? `Paiement : ${currency.format(request.payment.amount)} encaissés`
                  : request.payment.status === "cancelled"
                    ? "Paiement : autorisation annulée"
                    : request.payment.status === "abandoned"
                      ? "Paiement : demande abandonnée avant autorisation"
                    : `Paiement : ${currency.format(request.payment.amount)} autorisés — non encaissés`}
              </Text>
              {request.payment.initialAuthorizationAmount != null && (
                <Text size={200}>
                  {request.requestKind === "additionalMachine"
                    ? "Autorisation maximale"
                    : "Autorisation initiale"}{" "}
                  :{" "}
                  {currency.format(request.payment.initialAuthorizationAmount)}
                </Text>
              )}
              {request.payment.preparationAmount != null && (
                <Text size={200}>
                  Préparation :{" "}
                  {currency.format(request.payment.preparationAmount)}
                </Text>
              )}
              {request.payment.serviceAmountCents != null && (
                <Text size={200}>
                  Service proratisé :{" "}
                  {currency.format(request.payment.serviceAmountCents / 100)}
                </Text>
              )}
              {request.payment.status === "captured" &&
                request.requestKind !== "additionalDocuments" && (
                <Text size={200}>Crédit inclus : {currency.format(10)}</Text>
              )}
            </Card>
          )}
          {request.requestKind === "additionalDocuments" &&
            request.status === "treated" &&
            request.payment?.status === "captured" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">Documents reçus</Text>
              </Card>
            )}
          {request.status === "treated" &&
            request.payment?.status === "captured" &&
            request.preparationStatus === "ready" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">
                  {request.requestKind === "additionalDocuments"
                    ? "Documents intégrés"
                    : "Machine prête"}
                </Text>
                {request.readyAtUtc && <Text size={200}>{date.format(new Date(request.readyAtUtc))}</Text>}
              </Card>
            )}
          {request.status === "treated" &&
            request.payment?.status === "captured" &&
            request.preparationStatus !== "ready" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text>
                  {request.requestKind === "additionalDocuments"
                    ? "À utiliser uniquement lorsque les nouveaux documents ont été intégrés."
                    : "À utiliser uniquement lorsque la préparation et l’intégration documentaire sont terminées."}
                </Text>
                <Button appearance="primary" disabled={isUpdatingStatus || !workflow.actions.markReady.enabled} onClick={() => void markReady()}>
                  {request.requestKind === "additionalDocuments"
                    ? "Marquer les documents comme intégrés"
                    : "Marquer la machine comme prête"}
                </Button>
                {actionHint("markReady")}
              </Card>
            )}
          {request.requestKind === "additionalDocuments" &&
            request.status === "rejected" &&
            request.payment?.status === "cancelled" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">Autorisation annulée</Text>
              </Card>
            )}
          {request.requestKind === "additionalDocuments" &&
            request.status === "rejected" &&
            request.payment?.status === "abandoned" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">Demande abandonnée avant autorisation</Text>
              </Card>
            )}
          {request.requestKind === "additionalMachine" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "businessEntitiesCreated" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">
                  Machine créée — provisioning à finaliser
                </Text>
                <Text>Étape : BusinessEntitiesCreated</Text>
                <Button
                  appearance="primary"
                  disabled={isUpdatingStatus || !workflow.actions.prepareBilling.enabled}
                  onClick={() => void prepareBilling()}
                >
                  Configurer l’abonnement
                </Button>
                {actionHint("prepareBilling")}
              </Card>
            )}
          {requestKind === "initialMachine" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text className={styles.cardTitle}>Provisionnement</Text>
                {request.payment.provisioningStage === "amountFinalized" ? (
                  <>
                    <div className={styles.provisioningFields}>
                      <label className={styles.provisioningField}>
                        Entreprise
                        <select
                          className={styles.select}
                          aria-label="Entreprise"
                          disabled={isLoadingProvisioning || isUpdatingStatus}
                          value={selectedCompanyId}
                          onChange={(event) => {
                            setSelectedCompanyId(event.target.value);
                            setSelectedMachineId("");
                          }}
                        >
                          <option value="">Sélectionner une entreprise</option>
                          {companies.map((company) => (
                            <option key={company.id} value={company.id}>
                              {company.name}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className={styles.provisioningField}>
                        Machine
                        <select
                          className={styles.select}
                          aria-label="Machine"
                          disabled={
                            !selectedCompanyId ||
                            isLoadingProvisioning ||
                            isUpdatingStatus
                          }
                          value={selectedMachineId}
                          onChange={(event) =>
                            setSelectedMachineId(event.target.value)
                          }
                        >
                          <option value="">Sélectionner une machine</option>
                          {machines
                            .filter(
                              (machine) =>
                                machine.companyId === selectedCompanyId,
                            )
                            .map((machine) => (
                              <option key={machine.id} value={machine.id}>
                                {machine.name}
                                {machine.reference
                                  ? ` · ${machine.reference}`
                                  : ""}
                              </option>
                            ))}
                        </select>
                      </label>
                    </div>
                    <Button
                      appearance="primary"
                      disabled={
                        !selectedCompanyId ||
                        !selectedMachineId ||
                        isLoadingProvisioning ||
                        isUpdatingStatus ||
                        !workflow.actions.attach.enabled
                      }
                      onClick={() => void attachBusinessEntities()}
                    >
                      Confirmer le rattachement
                    </Button>
                  </>
                ) : (
                  <>
                    <Text>
                      Entreprise :{" "}
                      <strong>
                        {companies.find(
                          (company) =>
                            company.id === request.payment?.companyId,
                        )?.name ?? request.payment.companyId}
                      </strong>
                    </Text>
                    <Text>
                      Machine :{" "}
                      <strong>
                        {machines.find(
                          (machine) =>
                            machine.id === request.payment?.machineId,
                        )?.name ?? request.payment.machineId}
                      </strong>
                    </Text>
                    <Text weight="semibold">Rattachement effectué</Text>
                  </>
                )}
              </Card>
            )}
          {requestKind === "initialMachine" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "businessEntitiesCreated" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text>
                  Facturation : <strong>À préparer</strong>
                </Text>
                <Button
                  appearance="primary"
                  disabled={isUpdatingStatus || !workflow.actions.prepareBilling.enabled}
                  onClick={() => void prepareBilling()}
                >
                  Préparer la facturation
                </Button>
                {actionHint("prepareBilling")}
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage &&
            [
              "customerLinked",
              "subscriptionCreated",
              "initialPeriodCreated",
              "completed",
            ].includes(request.payment.provisioningStage) && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">
                  Facturation :{" "}
                  {request.requestKind === "additionalMachine"
                    ? "Customer Stripe existant vérifié"
                    : "Client Stripe rattaché"}
                </Text>
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "customerLinked" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text>
                  Abonnement : <strong>À configurer</strong>
                </Text>
                <Button
                  appearance="primary"
                  disabled={isUpdatingStatus || !workflow.actions.configureSubscription.enabled}
                  onClick={() => void configureSubscription()}
                >
                  Configurer l’abonnement
                </Button>
                {actionHint("configureSubscription")}
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage &&
            [
              "subscriptionCreated",
              "initialPeriodCreated",
              "completed",
            ].includes(request.payment.provisioningStage) && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">
                  {request.requestKind === "additionalMachine" &&
                  request.payment.provisioningStage === "subscriptionCreated"
                    ? "Abonnement mis à jour — activation à finaliser"
                    : "Abonnement : Abonnement configuré"}
                </Text>
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "subscriptionCreated" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text>
                  Activation : <strong>À finaliser</strong>
                </Text>
                <Button
                  appearance="primary"
                  disabled={isUpdatingStatus || !workflow.actions.activate.enabled}
                  onClick={() => void activateMachine()}
                >
                  {isUpdatingStatus
                    ? "Activation en cours…"
                    : request.requestKind === "additionalMachine"
                      ? "Finaliser l’activation"
                      : "Activer la machine"}
                </Button>
                {actionHint("activate")}
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "initialPeriodCreated" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text>
                  {request.requestKind === "additionalMachine" ? (
                    "Période initiale créée — activation à finaliser"
                  ) : (
                    <>
                      <span>Activation : </span>
                      <strong>À finaliser</strong>
                    </>
                  )}
                </Text>
                <Button
                  appearance="primary"
                  disabled={isUpdatingStatus || !workflow.actions.activate.enabled}
                  onClick={() => void activateMachine()}
                >
                  {isUpdatingStatus
                    ? "Activation en cours…"
                    : request.requestKind === "additionalMachine"
                      ? "Finaliser l’activation"
                      : "Activer la machine"}
                </Button>
                {actionHint("activate")}
              </Card>
            )}
          {request.requestKind !== "additionalDocuments" &&
            request.payment?.status === "captured" &&
            request.payment.provisioningStage === "completed" && (
              <Card className={`${styles.detailCard} ${styles.fullWidth}`}>
                <Text weight="semibold">Activation : Machine activée</Text>
                <Text>
                  {request.requestKind === "additionalMachine"
                    ? "10,00 € de crédit Agent disponibles"
                    : "10,00 € de crédit inclus disponibles"}
                </Text>
                {request.requestKind === "additionalMachine" && (
                  <Text>Documents reçus</Text>
                )}
              </Card>
            )}
        </div>
        {actionError && (
          <Text className={styles.error} role="alert">
            {actionError}
          </Text>
        )}
        {request.status === "pending" &&
          (request.requestKind !== "additionalDocuments" ||
            request.payment?.status === "authorized") && (
          <div className={styles.actions}>
            <Button
              appearance="secondary"
              disabled={isUpdatingStatus}
              onClick={() => void changeStatus("rejected")}
            >
              {request.requestKind === "additionalDocuments"
                ? "Refuser"
                : "Refuser la demande"}
            </Button>
            <Button
              appearance="primary"
              disabled={
                isUpdatingStatus || !workflow.actions.accept.enabled
              }
              onClick={() => void changeStatus("treated")}
            >
              {request.requestKind === "additionalDocuments"
                ? "Accepter"
                : "Accepter la demande"}
            </Button>
          </div>
        )}
        {archiveDialog}
      </ViewRoot>
    );
  }

  const renderRequestList = (items: MachineRequestListItem[]) => <>
    <div className={mergeClasses(styles.listHeader, styles.requestTableGrid)}>
      <Text>Date</Text><Text>Entreprise</Text><Text>Demandeur</Text><Text>Machine</Text>
      <Text>PDF</Text><Text>Pages</Text><Text>Préparation</Text><Text>Statut</Text>
    </div>
    <div className={styles.list}>
      {items.map(request => <Card key={request.requestId} className={mergeClasses(styles.requestRow, styles.requestTableGrid, request.status === "pending" && styles.waitingRow)}>
        <span className={`${styles.cell} ${styles.dateCell}`}><span data-mobile-field="date"><span className={styles.mobileLabel}>Date :</span>{date.format(new Date(request.createdAt))}</span><span className={styles.requestType} data-mobile-field="type"><span className={styles.compactOnlyLabel}>Type de demande :</span><Text size={200}>{requestKindLabel(request.requestKind)}</Text></span></span>
        <span className={`${styles.cell} ${styles.companyCell}`} data-mobile-field="company"><span className={styles.mobileLabel}>Entreprise :</span>{request.company}</span>
        <span className={`${styles.cell} ${styles.desktopOnly}`}><span className={styles.mobileLabel}>Demandeur :</span>{request.firstName} {request.lastName}<br/><Text size={200}>{request.email}<br/>{request.phone}</Text></span>
        <span className={`${styles.cell} ${styles.desktopOnly}`}><span className={styles.mobileLabel}>Machine :</span>{request.machineName}<br/>{request.requestKind !== "additionalDocuments" && <Text size={200}>{request.manufacturer} · {request.model}</Text>}</span>
        <span className={`${styles.cell} ${styles.desktopOnly}`}><span className={styles.mobileLabel}>PDF :</span>{request.documentCount}</span>
        <span className={`${styles.cell} ${styles.desktopOnly}`}><span className={styles.mobileLabel}>Pages :</span>{request.totalPages}</span>
        <span className={`${styles.cell} ${styles.desktopOnly}`}><span className={styles.mobileLabel}>Préparation :</span>{currency.format(request.preparationTotal)} HT</span>
        <span className={`${styles.cell} ${styles.rowActions}`}><span data-mobile-field="status"><StatusBadge status={request.status}/></span>
          <span className={styles.compactAction} data-mobile-field="action"><Button appearance="subtle" size="small" onClick={() => void openRequest(request.requestId)}>Voir la demande</Button></span>
        </span>
      </Card>)}
    </div>
  </>;

  return (
    <ViewRoot
      title="Nouvelles demandes"
      subtitle="Consultez les demandes de création de machine transmises depuis DiagLink."
    >
      {isLoading || isLoadingDetail ? (
        <div className={styles.state}>
          <Spinner
            label={
              isLoadingDetail
                ? "Chargement de la demande…"
                : "Chargement des demandes…"
            }
          />
        </div>
      ) : detailError ? (
        <div className={styles.state}>
          <div>
            <Text className={styles.error} role="alert">
              {detailError}
            </Text>
            <br />
            <Button onClick={() => setDetailError(null)}>
              Retour aux demandes
            </Button>
          </div>
        </div>
      ) : hasLoadingError ? (
        <div className={styles.state}>
          <div>
            <Text className={styles.error} role="alert">
              Impossible de charger les nouvelles demandes.
            </Text>
            <br />
            <Button onClick={() => void onRetry()}>Réessayer</Button>
          </div>
        </div>
      ) : (
        <>
          {activeRequests.length === 0 ? <div className={styles.state}><div><Text weight="semibold">Aucune nouvelle demande.</Text><br/><Text size={200}>Les nouvelles demandes transmises depuis le formulaire DiagLink apparaîtront ici.</Text></div></div> : renderRequestList(activeRequests)}
          <details className={styles.history} onToggle={handleHistoryToggle}>
            <summary ref={historySummaryRef} className={styles.historySummary}>Historique des demandes ({archivedRequests.length})</summary>
            <div className={styles.historyContent}>
              <Input className={styles.historySearch} aria-label="Rechercher dans l’historique" placeholder="Rechercher dans l’historique..." value={historySearch} onChange={(_event, data) => setHistorySearch(data.value)}/>
              {historyError ? <Text className={styles.error} role="alert">Impossible de charger l’historique des demandes.</Text>
                : filteredArchivedRequests.length === 0 ? <Text>Aucune demande trouvée dans l’historique.</Text>
                  : renderRequestList(filteredArchivedRequests)}
            </div>
          </details>
        </>
      )}
      {archiveDialog}
    </ViewRoot>
  );
}
