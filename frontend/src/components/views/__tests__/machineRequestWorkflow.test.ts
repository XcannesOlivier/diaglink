import { describe, expect, it } from "vitest";
import type { MachineRequestDetail } from "../../../services/machineRequestAdminApi";
import { getMachineRequestWorkflow } from "../machineRequestWorkflow";

const base: MachineRequestDetail = {
  requestId: "request-1",
  createdAt: "2026-10-01T08:00:00Z",
  status: "treated",
  client: { firstName: "Claire", lastName: "Martin", company: "Develon", email: "claire@example.com", phone: "0600000000" },
  machine: { machineName: "Machine A", manufacturer: "Develon", model: "DX", serialNumber: null, description: null },
  documents: [],
  pricing: { totalPages: 10, includedPages: 400, additionalPages: 0, basePreparationPrice: 99.9, additionalPagePrice: .27, preparationTotal: 99.9, monthlySubscriptionPrice: 29.9 },
  requestKind: "initialMachine",
  preparationStatus: "pending",
  payment: {
    paymentRequestId: "payment-1", status: "captured", amount: 110, currency: "EUR",
    authorizationInsufficient: false, provisioningStage: "amountFinalized", companyId: null, machineId: null,
  },
};

const enabledActions = (request: MachineRequestDetail) => Object.entries(getMachineRequestWorkflow(request).actions)
  .filter(([, state]) => state.enabled).map(([action]) => action);

describe("getMachineRequestWorkflow", () => {
  it("guides an initial machine from payment through provisioning", () => {
    const paid = getMachineRequestWorkflow(base);
    expect(paid.steps.find(step => step.id === "payment")?.state).toBe("completed");
    expect(paid.currentStep.id).toBe("provisioning");
    expect(enabledActions(base)).toEqual(["attach"]);
    expect(paid.actions.markReady.enabled).toBe(false);

    const attached = { ...base, payment: { ...base.payment!, provisioningStage: "businessEntitiesCreated" as const, companyId: "company-1", machineId: "machine-1" } };
    const workflow = getMachineRequestWorkflow(attached);
    expect(workflow.steps.find(step => step.id === "provisioning")?.state).toBe("completed");
    expect(workflow.currentStep.id).toBe("readiness");
    expect(enabledActions(attached)).toEqual(["markReady"]);
  });

  it("unlocks billing only after the machine is ready", () => {
    const attached = { ...base, payment: { ...base.payment!, provisioningStage: "businessEntitiesCreated" as const, companyId: "company-1", machineId: "machine-1" } };
    expect(getMachineRequestWorkflow(attached).actions.prepareBilling.enabled).toBe(false);

    const ready = { ...attached, preparationStatus: "ready" as const };
    const workflow = getMachineRequestWorkflow(ready);
    expect(workflow.steps.find(step => step.id === "readiness")?.state).toBe("completed");
    expect(workflow.currentStep.id).toBe("billing");
    expect(enabledActions(ready)).toEqual(["prepareBilling"]);
  });

  it("unlocks activation after billing and completes from backend checkpoints", () => {
    const configured = { ...base, preparationStatus: "ready" as const, payment: { ...base.payment!, provisioningStage: "subscriptionCreated" as const, companyId: "company-1", machineId: "machine-1" } };
    const workflow = getMachineRequestWorkflow(configured);
    expect(workflow.steps.find(step => step.id === "billing")?.state).toBe("completed");
    expect(workflow.currentStep.id).toBe("activation");
    expect(enabledActions(configured)).toEqual(["activate"]);

    const completed = { ...configured, payment: { ...configured.payment!, provisioningStage: "completed" as const } };
    expect(getMachineRequestWorkflow(completed).steps.every(step => step.state === "completed")).toBe(true);
    expect(enabledActions(completed)).toEqual([]);
  });

  it("uses the existing customer path for an additional machine without an enterprise-creation step", () => {
    const request = { ...base, requestKind: "additionalMachine" as const, preparationStatus: "ready" as const,
      payment: { ...base.payment!, provisioningStage: "businessEntitiesCreated" as const, companyId: "company-1", machineId: "machine-2" } };
    const workflow = getMachineRequestWorkflow(request);
    expect(workflow.steps.map(step => step.label)).toContain("Création de la machine");
    expect(workflow.steps.map(step => step.label).join(" ")).not.toContain("création d’entreprise");
    expect(enabledActions(request)).toEqual(["prepareBilling"]);
  });

  it("keeps additional documents on a short workflow without billing or activation", () => {
    const request = { ...base, requestKind: "additionalDocuments" as const };
    const workflow = getMachineRequestWorkflow(request);
    expect(workflow.steps.map(step => step.id)).toEqual(["payment", "received", "integration", "done"]);
    expect(workflow.steps.map(step => step.label).join(" ")).not.toMatch(/abonnement|activation/i);
    expect(enabledActions(request)).toEqual(["markReady"]);
  });

  it("rebuilds the same workflow entirely from refreshed backend data", () => {
    const refreshed = { ...base, preparationStatus: "ready" as const,
      payment: { ...base.payment!, provisioningStage: "customerLinked" as const, companyId: "company-1", machineId: "machine-1" } };
    expect(getMachineRequestWorkflow(structuredClone(refreshed))).toEqual(getMachineRequestWorkflow(refreshed));
    expect(enabledActions(refreshed)).toEqual(["configureSubscription"]);
  });
});
