import { DocumentText20Regular } from '@fluentui/react-icons';
import styles from '../../pages/landing/LandingPage.module.css';

export function DocumentDiagram() {
  return (
    <div className={styles.diagram} role="img" aria-label="Extrait illustratif d'un schéma électrique">
      <div className={styles.documentTop}><DocumentText20Regular /><span>MANUEL ÉLECTRIQUE</span><b>47</b></div>
      <svg viewBox="0 0 260 150" role="img" aria-label="Schéma électrique simplifié">
        <path d="M20 28h48v28h45m28 0h45v-28h54M68 42v68h40m45 0h33V42" />
        <circle cx="127" cy="56" r="14" /><rect x="108" y="94" width="45" height="31" rx="3" />
        <path d="M127 70v24M153 109h34M42 110h26" /><text x="117" y="60">F2</text><text x="119" y="114">K1</text>
      </svg>
    </div>
  );
}

export function DiagnosticCard() {
  return (
    <div className={styles.demoVisual}>
      <div className={styles.demoConversation}>
        <p className={styles.demoQuestion}>Le compresseur ne démarre plus. J’ai bien du 400 V à l’entrée. Que dois-je contrôler ?</p>
        <div className={styles.demoAnswer}>
          <strong>DiagLink</strong>
          <ol><li>Mesurez la tension entre X1-12 et X1-14.</li><li>Si aucune tension n’est présente, vérifiez le fusible F2.</li><li>Contrôlez l’état du relais K1.</li><li>En cas de remplacement, utilisez la référence indiquée dans le manuel.</li></ol>
          <div className={styles.source}><DocumentText20Regular /><span><b>Source : Manuel électrique</b><small>Page 47</small></span></div>
        </div>
      </div>
      <DocumentDiagram />
    </div>
  );
}
