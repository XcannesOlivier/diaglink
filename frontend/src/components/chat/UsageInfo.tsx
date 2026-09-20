import { Body1, Text, Divider } from '@fluentui/react-components';
import { InfoRegular } from '@fluentui/react-icons';
import type { IUsageInfo } from '../../types/chat';
import styles from './UsageInfo.module.css';

interface UsageInfoProps {
  info: IUsageInfo;
  duration?: number;
}

export const UsageInfo: React.FC<UsageInfoProps> = ({ info, duration }) => {
  const durationMs = duration ?? info.duration;
  const available = info.available !== false && info.promptTokens != null && info.completionTokens != null;
  const totalTokens = available ? info.totalTokens ?? (info.promptTokens! + info.completionTokens!) : null;
  
  return (
    <div className={styles.usageInfoContainer}>
      <div className={styles.usageSummary}>
        {durationMs !== undefined && (
          <>
            <span>{durationMs.toFixed(0)}ms</span>
            <span className={styles.divider}>|</span>
          </>
        )}
        <span>{totalTokens === null ? 'Usage inconnu' : `${totalTokens} jetons`}</span>
        <button 
          className={styles.infoButton}
          title="Informations sur l'utilisation"
          aria-label="Afficher les détails d'utilisation des jetons"
          type="button"
        >
          <InfoRegular className={styles.infoIcon} />
        </button>
      </div>
      <div className={styles.usageDetails}>
        <Text weight="semibold" size={200}>Informations sur l'utilisation</Text>
        <Divider className={styles.detailsDivider} />
        <div className={styles.detailsList}>
          <div className={styles.detailsItem}>
            <Body1 className={styles.detailLabel}>Entrée</Body1>
            <Body1 className={styles.detailValue}>{available ? `${info.promptTokens} jetons` : 'Inconnu'}</Body1>
          </div>
          <div className={styles.detailsItem}>
            <Body1 className={styles.detailLabel}>Sortie</Body1>
            <Body1 className={styles.detailValue}>{available ? `${info.completionTokens} jetons` : 'Inconnu'}</Body1>
          </div>
        </div>
      </div>
    </div>
  );
};
