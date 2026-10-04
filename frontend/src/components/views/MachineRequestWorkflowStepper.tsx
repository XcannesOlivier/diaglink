import { Text, makeStyles, tokens } from "@fluentui/react-components";
import type { MachineRequestWorkflow as Workflow } from "./machineRequestWorkflow";

const useStyles = makeStyles({
  root: { display: "grid", gap: tokens.spacingVerticalM, marginBottom: tokens.spacingVerticalL },
  steps: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fit,minmax(130px,1fr))",
    gap: tokens.spacingHorizontalS,
    padding: 0,
    margin: 0,
    listStyle: "none",
    "@media (max-width: 700px)": { gridTemplateColumns: "minmax(0,1fr)" },
  },
  step: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    minWidth: 0,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  completed: { color: tokens.colorPaletteGreenForeground1, border: `1px solid ${tokens.colorPaletteGreenBorder1}` },
  current: { color: tokens.colorBrandForeground1, border: `1px solid ${tokens.colorBrandStroke1}`, backgroundColor: tokens.colorBrandBackground2 },
  error: { color: tokens.colorPaletteRedForeground1, border: `1px solid ${tokens.colorPaletteRedBorder1}` },
  marker: {
    display: "inline-grid",
    placeItems: "center",
    flex: "0 0 24px",
    width: "24px",
    height: "24px",
    borderRadius: tokens.borderRadiusCircular,
    border: `1px solid currentColor`,
    fontWeight: tokens.fontWeightSemibold,
  },
  label: { minWidth: 0, overflowWrap: "anywhere" },
  currentCard: {
    display: "grid",
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalM,
    borderLeft: `4px solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  visuallyHidden: {
    position: "absolute",
    width: "1px",
    height: "1px",
    padding: 0,
    margin: "-1px",
    overflow: "hidden",
    clip: "rect(0,0,0,0)",
    whiteSpace: "nowrap",
    border: 0,
  },
});

export function MachineRequestWorkflowStepper({ workflow }: { workflow: Workflow }) {
  const styles = useStyles();
  return <section className={styles.root} aria-label="Progression de la demande">
    <ol className={styles.steps}>
      {workflow.steps.map((step, index) => <li key={step.id} data-workflow-step={step.id} data-state={step.state}
        className={`${styles.step} ${step.state === "completed" ? styles.completed : step.state === "current" ? styles.current : step.state === "error" ? styles.error : ""}`}>
        <span className={styles.marker} aria-hidden="true">{step.state === "completed" ? "✓" : step.state === "current" ? "→" : step.state === "error" ? "!" : index + 1}</span>
        <Text className={styles.label} weight={step.state === "current" ? "semibold" : "regular"}>{index + 1}. {step.label}</Text>
        <span className={styles.visuallyHidden}>{step.state === "completed" ? "Terminée" : step.state === "current" ? "Étape actuelle" : step.state === "error" ? "Intervention requise" : "Étape future"}</span>
      </li>)}
    </ol>
    <div className={styles.currentCard} aria-live="polite">
      <Text size={200}>Étape actuelle</Text>
      <Text weight="semibold">{workflow.currentStep.label}</Text>
      <Text>{workflow.currentStep.description}</Text>
    </div>
  </section>;
}
