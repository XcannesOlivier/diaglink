/** Mirrors backend/WebApp.Api/Models/MachineModels.cs exactly. */
export interface MachineDto {
  id: string;
  companyId: string;
  name: string;
  reference: string | null;
  status: string;
  hasAssistantConfigured: boolean;
  isAccessible: boolean;
}

/** The machine currently scoping the active/next chat conversation — never carries AgentId/ProjectEndpoint. */
export interface SelectedMachine {
  id: string;
  name: string;
  reference?: string | null;
}

export interface MachineDocumentDto {
  id: string;
  name: string;
}

/** Mirrors backend/WebApp.Api/Models/MachineAssignmentModels.cs UserMachineAccessDto. */
export interface UserMachineAccessDto {
  machineId: string;
  name: string;
  reference?: string | null;
  assigned: boolean;
}
