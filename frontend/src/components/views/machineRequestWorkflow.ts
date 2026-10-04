import type { MachineRequestDetail } from "../../services/machineRequestAdminApi";

export type MachineRequestWorkflowAction =
  | "accept"
  | "attach"
  | "markReady"
  | "prepareBilling"
  | "configureSubscription"
  | "activate";

export type MachineRequestWorkflowStepState = "completed" | "current" | "future" | "error";

export interface MachineRequestWorkflowStep {
  id: string;
  label: string;
  state: MachineRequestWorkflowStepState;
  description: string;
}

export interface MachineRequestWorkflowActionState {
  enabled: boolean;
  unavailableReason?: string;
}

export interface MachineRequestWorkflow {
  steps: MachineRequestWorkflowStep[];
  currentStep: MachineRequestWorkflowStep;
  actions: Record<MachineRequestWorkflowAction, MachineRequestWorkflowActionState>;
}

const provisioningStages = [
  "awaitingAcceptance",
  "amountFinalized",
  "businessEntitiesCreated",
  "customerLinked",
  "subscriptionCreated",
  "initialPeriodCreated",
  "completed",
] as const;

function stageReached(request: MachineRequestDetail, expected: typeof provisioningStages[number]) {
  const stage = request.payment?.provisioningStage;
  return stage !== undefined && provisioningStages.indexOf(stage) >= provisioningStages.indexOf(expected);
}

function actionStates(): MachineRequestWorkflow["actions"] {
  const unavailable = { enabled: false, unavailableReason: "Disponible après l’étape précédente." };
  return {
    accept: { ...unavailable },
    attach: { ...unavailable },
    markReady: { ...unavailable },
    prepareBilling: { ...unavailable },
    configureSubscription: { ...unavailable },
    activate: { ...unavailable },
  };
}

function buildSteps(
  definitions: Array<Omit<MachineRequestWorkflowStep, "state"> & { completed: boolean }>,
  currentId: string,
  errorId?: string,
) {
  return definitions.map(({ completed, ...step }): MachineRequestWorkflowStep => ({
    ...step,
    state: errorId === step.id ? "error" : completed ? "completed" : currentId === step.id ? "current" : "future",
  }));
}

function documentsWorkflow(request: MachineRequestDetail): MachineRequestWorkflow {
  const actions = actionStates();
  const paymentCompleted = request.status === "treated" && request.payment?.status === "captured";
  const ready = request.preparationStatus === "ready";
  const paymentFailed = request.status === "rejected" || request.payment?.status === "cancelled" || request.payment?.status === "abandoned";
  const canAccept = request.status === "pending" && request.payment?.status === "authorized" && !request.payment.authorizationInsufficient;
  if (canAccept) actions.accept = { enabled: true };
  if (paymentCompleted && !ready) actions.markReady = { enabled: true };

  const currentId = paymentFailed || !paymentCompleted ? "payment" : !ready ? "integration" : "done";
  const steps = buildSteps([
    { id: "payment", label: "Paiement", completed: paymentCompleted, description: paymentFailed
      ? "La demande a été refusée ou son autorisation de paiement n’est plus active."
      : canAccept ? "Validez la demande pour encaisser le paiement documentaire."
      : "Le paiement doit être autorisé avant que la demande puisse être validée." },
    { id: "received", label: "Documents reçus", completed: paymentCompleted, description: "Les documents ont été reçus et peuvent être préparés." },
    { id: "integration", label: "Intégration documentaire", completed: ready, description: "Terminez l’intégration, puis marquez les documents comme intégrés." },
    { id: "done", label: "Terminé", completed: ready, description: "Les nouveaux documents sont disponibles dans DiagLink." },
  ], currentId, paymentFailed ? "payment" : undefined);
  return { steps, currentStep: steps.find(step => step.id === currentId)!, actions };
}

export function getMachineRequestWorkflow(request: MachineRequestDetail): MachineRequestWorkflow {
  if (request.requestKind === "additionalDocuments") return documentsWorkflow(request);

  const actions = actionStates();
  const additional = request.requestKind === "additionalMachine";
  const paymentCompleted = request.payment?.status === "captured";
  const provisioned = stageReached(request, "businessEntitiesCreated");
  const ready = request.preparationStatus === "ready";
  const billingStarted = stageReached(request, "customerLinked");
  const billingCompleted = stageReached(request, "subscriptionCreated");
  const activated = stageReached(request, "completed");
  const paymentFailed = request.status === "rejected" || request.payment?.status === "cancelled" || request.payment?.status === "abandoned";
  const canAccept = request.status === "pending" && (!request.payment || request.payment.status === "authorized")
    && !request.payment?.authorizationInsufficient;

  if (canAccept) actions.accept = { enabled: true };
  if (!additional && paymentCompleted && request.payment?.provisioningStage === "amountFinalized") actions.attach = { enabled: true };
  if (request.status === "treated" && paymentCompleted && provisioned && !ready) actions.markReady = { enabled: true };
  if (ready && request.payment?.provisioningStage === "businessEntitiesCreated") actions.prepareBilling = { enabled: true };
  if (ready && request.payment?.provisioningStage === "customerLinked") actions.configureSubscription = { enabled: true };
  if (ready && (request.payment?.provisioningStage === "subscriptionCreated" || request.payment?.provisioningStage === "initialPeriodCreated"))
    actions.activate = { enabled: true };

  if (!provisioned) {
    actions.markReady.unavailableReason = additional
      ? "Disponible après la création de la machine."
      : "Disponible après le rattachement de l’entreprise et de la machine.";
  }
  if (!ready) {
    actions.prepareBilling.unavailableReason = "Disponible lorsque la machine est marquée comme prête.";
    actions.configureSubscription.unavailableReason = "Disponible lorsque la machine est marquée comme prête.";
    actions.activate.unavailableReason = "Disponible lorsque la machine est prête et la facturation configurée.";
  }

  let currentId: string;
  if (paymentFailed || !paymentCompleted) currentId = "payment";
  else if (!provisioned) currentId = "provisioning";
  else if (!ready) currentId = "readiness";
  else if (!billingCompleted) currentId = "billing";
  else if (!activated) currentId = "activation";
  else currentId = "activation";

  const steps = buildSteps([
    { id: "payment", label: "Paiement", completed: paymentCompleted, description: paymentFailed
      ? "La demande a été refusée ou son paiement n’est plus actif."
      : canAccept ? "Validez la demande pour confirmer le paiement."
      : "Le paiement doit être autorisé avant de poursuivre." },
    { id: "provisioning", label: additional ? "Création de la machine" : "Rattachement", completed: provisioned,
      description: additional ? "La création et le rattachement de la machine doivent être finalisés."
        : "Rattachez la demande à l’entreprise et à la machine concernées." },
    { id: "readiness", label: "Documents / machine prête", completed: ready,
      description: "Terminez la préparation documentaire, puis marquez la machine comme prête." },
    { id: "billing", label: additional ? "Mise à jour de l’abonnement" : "Facturation / abonnement", completed: billingCompleted,
      description: !billingStarted ? "La machine est prête. Préparez maintenant la facturation."
        : "Le client Stripe est rattaché. Configurez maintenant l’abonnement." },
    { id: "activation", label: "Activation", completed: activated,
      description: activated ? "Le parcours est terminé et la machine est activée."
        : "La facturation est configurée. Vous pouvez maintenant activer la machine." },
  ], currentId, paymentFailed ? "payment" : undefined);

  return { steps, currentStep: steps.find(step => step.id === currentId)!, actions };
}
