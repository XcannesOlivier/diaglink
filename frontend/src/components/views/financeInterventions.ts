export interface GlobalFinanceIntervention {
  id: string;
  type: 'wallet-top-up';
  label: string;
}

export interface MachineFinanceIntervention {
  id: string;
  type: 'machine-addition';
  machineId: string;
  label: string;
}
