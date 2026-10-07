import { useEffect, useState } from 'react';
import { Button, Select, Spinner, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useAuth } from '../../hooks/useAuth';
import { getUsageSummary, getUsageCompanies, getUsageMachines, getUsageUsers,
  getCompanyUsageSummary, getCompanyUsageMachines, getCompanyUsageUsers } from '../../services/aiUsageService';
import type { AiUsageSummaryDto, CompanyUsageDto, MachineUsageDto, UserUsageDto, UsageType } from '../../types/aiUsage';
import { usageWindow, usagePeriods, formatUsageTokens, usageNotice, type UsagePeriod } from '../../utils/aiUsage';

const useStyles = makeStyles({
  root: { gridColumn: '1 / -1', minWidth: 0, padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium, border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2 },
  controls: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalM, marginBlock: tokens.spacingVerticalM },
  metrics: { display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: tokens.spacingHorizontalM,
    '@media (max-width: 1000px)': { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' },
    '@media (max-width: 600px)': { gridTemplateColumns: 'minmax(0, 1fr)' } },
  companyMetrics: { gridTemplateColumns: 'repeat(3, minmax(0, 1fr))' },
  metric: { minWidth: 0, overflowWrap: 'anywhere', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground1, borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}` },
  muted: { color: tokens.colorNeutralForeground3 },
  details: { display: 'block', marginBlock: tokens.spacingVerticalM },
  scroll: { overflowX: 'auto' },
  table: { width: '100%', borderCollapse: 'collapse',
    '& th, & td': { textAlign: 'left', padding: tokens.spacingHorizontalM, borderBottom: `1px solid ${tokens.colorNeutralStroke2}` } },
});
type Scope = { id: string | null; name: string };
type Row = { id: string | null; name: string; metrics: CompanyUsageDto['metrics']; machines?: number; users?: number };
type Load<T> = { kind: 'empty' } | { kind: 'loading' } | { kind: 'error' } | { kind: 'success'; data: T };

export function AiUsagePanel({ scope = 'super-admin', companyName, selectedCompany }: {
  scope?: 'super-admin' | 'company'; companyName?: string; selectedCompany?: Scope;
}) {
  const isCompany = scope === 'company';
  const styles = useStyles();
  const { getAccessToken } = useAuth();
  const [period, setPeriod] = useState<UsagePeriod>('month');
  const [type, setType] = useState<UsageType | undefined>();
  const [filter, setFilter] = useState(() => usageWindow('month'));
  const [company, setCompany] = useState<Scope | null>(selectedCompany ?? null);
  const [machine, setMachine] = useState<Scope | null>(null);
  const showMachines = isCompany || company !== null;
  const [summary, setSummary] = useState<Load<AiUsageSummaryDto>>({ kind: 'loading' });
  const [rows, setRows] = useState<Load<Row[]>>({ kind: 'loading' });

  useEffect(() => {
    const controller = new AbortController();
    setSummary({ kind: 'loading' });
    if (selectedCompany) {
      void getUsageCompanies(getAccessToken, filter, controller.signal).then(result => {
        if (controller.signal.aborted) return;
        const row = result.kind === 'success' ? result.data.find(c => c.companyId === selectedCompany.id) : undefined;
        setSummary(row ? {kind:'success',data:{from:filter.from??null,to:filter.to,usageType:filter.usageType??null,metrics:row.metrics,unassignedCompanyCount:0,unassignedMachineCount:0,unassignedUserCount:0}} : {kind:result.kind==='success'?'empty':'error'});
      });
      return () => controller.abort();
    }
    const loadSummary = isCompany ? getCompanyUsageSummary : getUsageSummary;
    void loadSummary(getAccessToken, filter, controller.signal).then(result => {
      if (!controller.signal.aborted) setSummary(result.kind === 'success' ? { kind: 'success', data: result.data } : { kind: 'error' });
    });
    return () => controller.abort();
  }, [getAccessToken, filter, isCompany, selectedCompany]);

  useEffect(() => {
    const controller = new AbortController();
    setRows({ kind: 'loading' });
    const load = async () => {
      let data: Row[];
      if (machine && (isCompany || company)) {
        const result = isCompany
          ? await getCompanyUsageUsers(getAccessToken, machine.id, filter, controller.signal)
          : await getUsageUsers(getAccessToken, company!.id, machine.id, filter, controller.signal);
        if (result.kind !== 'success') throw new Error();
        data = result.data.map((r: UserUsageDto) => ({ id: r.userId, name: r.userDisplayName, metrics: r.metrics }));
      } else if (isCompany || company) {
        const result = isCompany
          ? await getCompanyUsageMachines(getAccessToken, filter, controller.signal)
          : await getUsageMachines(getAccessToken, company!.id, filter, controller.signal);
        if (result.kind !== 'success') throw new Error();
        data = result.data.map((r: MachineUsageDto) => ({ id: r.machineId, name: r.machineName, users: r.userCountUsed, metrics: r.metrics }));
      } else {
        const result = await getUsageCompanies(getAccessToken, filter, controller.signal);
        if (result.kind !== 'success') throw new Error();
        data = result.data.map((r: CompanyUsageDto) => ({ id: r.companyId, name: r.companyName, machines: r.machineCountUsed, users: r.userCountUsed, metrics: r.metrics }));
      }
      if (!controller.signal.aborted) setRows({ kind: 'success', data });
    };
    void load().catch(() => { if (!controller.signal.aborted) setRows({ kind: 'error' }); });
    return () => controller.abort();
  }, [getAccessToken, filter, company, machine, isCompany]);

  const refresh = (p = period, t = type) => {
    setSummary({ kind: 'loading' }); setRows({ kind: 'loading' });
    setFilter(usageWindow(p, t));
  };
  const changeLevel = (c: Scope | null, m: Scope | null) => {
    setRows({ kind: 'loading' }); setCompany(c); setMachine(m);
  };
  const m = summary.kind === 'success' ? summary.data.metrics : null;
  return <section className={styles.root} aria-label="Suivi de consommation">
    {!isCompany && <Text as="h2" size={500} weight="semibold">Suivi de consommation</Text>}
    <div className={styles.controls}>
      <Select aria-label="Période" value={period} onChange={(_, data) => {
        const p = data.value as UsagePeriod; setPeriod(p); refresh(p, type);
      }}>
        {Object.entries(usagePeriods).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </Select>
      <Select aria-label="Type d'usage" value={type ?? ''} onChange={(_, data) => {
        const t = data.value ? data.value as UsageType : undefined; setType(t);
        setSummary({ kind: 'loading' }); setRows({ kind: 'loading' }); setFilter(usageWindow(period, t));
      }}>
        <option value="">Tous les usages</option><option value="ChatResponse">Utilisateur</option>
        <option value="VisionTool">Vision</option><option value="ConversationSummary">Interne</option>
      </Select>
      <Button onClick={() => refresh()}>Actualiser</Button>
    </div>
    <div aria-live="polite">
      {summary.kind === 'empty' && <Text>Aucun usage pour cette entreprise sur cette période.</Text>}
      {summary.kind === 'loading' && <Spinner size="small" label="Chargement de la consommation" />}
      {summary.kind === 'error' && <Text role="alert">Impossible de charger le résumé de consommation.</Text>}
      {m && <>
        <div className={mergeClasses(styles.metrics, isCompany && styles.companyMetrics)}>
          {[
            ['Tokens connus', formatUsageTokens(m.totalTokens)], ['Input', formatUsageTokens(m.inputTokens)],
            ['Cache lu', formatUsageTokens(m.cacheReadInputTokens ?? null)],
            ['Cache créé', formatUsageTokens(m.cacheCreationInputTokens ?? null)],
            ['Cache 5 min', formatUsageTokens(m.cacheCreation5mInputTokens ?? null)],
            ['Cache 1 h', formatUsageTokens(m.cacheCreation1hInputTokens ?? null)],
            ['Output', formatUsageTokens(m.outputTokens)],
            ...(!isCompany ? [['Réponses IA', m.chatResponseCount.toLocaleString('fr-FR')]] : []),
          ].map(([label, value]) => <div key={label} className={styles.metric}>
            <Text className={styles.muted}>{label}</Text><Text size={800} weight="semibold">{value}</Text>
          </div>)}
        </div>
        {!isCompany && <Text className={styles.details}>Analyses Vision : {m.visionToolCount} · Résumés internes : {m.conversationSummaryCount} · Usages inconnus : {m.unknownUsageCount}</Text>}
        {m.knownUsageCount === 0 && usageNotice(m) && <Text className={styles.details}>{usageNotice(m)}</Text>}
      </>}
    </div>
    {((!isCompany && company) || machine) && <div className={styles.controls}>
      {!isCompany && !selectedCompany && company && <Button onClick={() => changeLevel(null, null)}>Toutes les entreprises</Button>}
      {machine && <Button onClick={() => changeLevel(company, null)}>Retour aux machines</Button>}
    </div>}
    <Text as="h3" weight="semibold">{isCompany
      ? machine ? `${companyName ?? 'Entreprise'} > ${machine.name} > Utilisateurs` : 'Consommation par machine'
      : company
      ? machine ? `${company.name} > ${machine.name} > Utilisateurs` : `${company.name} > Machines`
      : 'Consommation par entreprise'}</Text>
    {rows.kind === 'loading' && <Spinner size="small" label="Chargement du détail" />}
    {rows.kind === 'error' && <Text role="alert">Impossible de charger le détail de consommation.</Text>}
    {rows.kind === 'success' && (rows.data.length === 0
      ? <Text className={styles.details}>Aucune consommation historisée sur cette période.</Text>
      : <div className={styles.scroll}><table className={styles.table}>
        <thead><tr>
          <th scope="col">{machine ? 'Utilisateur' : showMachines ? 'Machine' : 'Entreprise'}</th>
          {!isCompany && <><th scope="col">Réponses IA</th><th scope="col">Vision</th><th scope="col">Résumés</th></>}
          {!showMachines && <th scope="col">Machines</th>}
          {!machine && <th scope="col">Utilisateurs</th>}
          {machine && <><th scope="col">Input</th><th scope="col">Cache lu</th><th scope="col">Cache créé</th><th scope="col">Cache 5 min</th><th scope="col">Cache 1 h</th><th scope="col">Output</th></>}
          <th scope="col">Tokens</th>{!isCompany && <th scope="col">Usages inconnus</th>}
        </tr></thead>
        <tbody>{rows.data.map(row => <tr key={row.id ?? 'unassigned'}>
          <td>{machine ? row.name : <Button appearance="subtle" onClick={() => {
            const scope = { id: row.id, name: row.name };
            changeLevel(isCompany ? null : company ?? scope, showMachines ? scope : null);
          }}>{row.name}</Button>}</td>
          {!isCompany && <><td>{row.metrics.chatResponseCount}</td><td>{row.metrics.visionToolCount}</td><td>{row.metrics.conversationSummaryCount}</td></>}
          {!showMachines && <td>{row.machines}</td>}{!machine && <td>{row.users}</td>}
          {machine && <><td>{formatUsageTokens(row.metrics.inputTokens)}</td><td>{formatUsageTokens(row.metrics.cacheReadInputTokens ?? null)}</td><td>{formatUsageTokens(row.metrics.cacheCreationInputTokens ?? null)}</td><td>{formatUsageTokens(row.metrics.cacheCreation5mInputTokens ?? null)}</td><td>{formatUsageTokens(row.metrics.cacheCreation1hInputTokens ?? null)}</td><td>{formatUsageTokens(row.metrics.outputTokens)}</td></>}
          <td>{formatUsageTokens(row.metrics.totalTokens)}{row.metrics.knownUsageCount > 0 && row.metrics.unknownUsageCount > 0 && ' (partiel)'}</td>
          {!isCompany && <td>{row.metrics.unknownUsageCount}</td>}
        </tr>)}</tbody>
      </table></div>)}
  </section>;
}
