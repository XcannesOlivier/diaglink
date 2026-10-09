import type { ReactNode } from 'react';
import { Body1, Subtitle1 } from '@fluentui/react-components';
import {
  BoxSearch20Regular,
  BuildingShop20Regular,
  DocumentSearch20Regular,
  Wrench20Regular,
} from '@fluentui/react-icons';
import styles from './StarterMessages.module.css';

interface IStarterMessageProps {
  agentName?: string;
  agentDescription?: string;
  accessory?: ReactNode;
}

const capabilities = [
  { label: 'Diagnostiquer une panne', icon: Wrench20Regular },
  { label: 'Identifier une pièce', icon: BoxSearch20Regular },
  { label: 'Comprendre un schéma', icon: DocumentSearch20Regular },
  { label: 'Trouver un fournisseur', icon: BuildingShop20Regular },
];

export const StarterMessages = ({
  agentName,
  agentDescription,
  accessory,
}: IStarterMessageProps): ReactNode => {
  const displayAgentName = agentName?.replace(/-/g, ' ').trim();
  const mobileTitlePrefix = 'Assistant Technique';
  const mobileMachineName = displayAgentName?.startsWith(mobileTitlePrefix)
    ? displayAgentName.slice(mobileTitlePrefix.length).trim()
    : displayAgentName;

  return (
    <div className={styles.zeroprompt}>
      <div className={styles.content}>
        <div className={`${styles.welcome} ${styles.desktopAgentTitle}`}>
          <Subtitle1 className={styles.mobileAgentTitleMain}>
            {mobileTitlePrefix}
          </Subtitle1>

          {mobileMachineName && mobileMachineName !== mobileTitlePrefix && (
            <Subtitle1 className={styles.mobileAgentMachineName}>
              {mobileMachineName}
          </Subtitle1>
        )}
      </div>
        <div className={styles.mobileAgentTitle}>
          <Subtitle1 className={styles.mobileAgentTitleMain}>{mobileTitlePrefix}</Subtitle1>
          {mobileMachineName && mobileMachineName !== mobileTitlePrefix && (
            <Subtitle1 className={styles.mobileAgentMachineName}>{mobileMachineName}</Subtitle1>
          )}
        </div>
        {agentDescription && (
          <Body1 className={styles.caption}>{agentDescription}</Body1>
        )}
      </div>

      {accessory}

      <div className={styles.capabilitySection}>
        <Body1 className={styles.capabilityIntro}>DiagLink vous aide à :</Body1>
        <ul className={styles.capabilityList}>
          {capabilities.map(({ label, icon: Icon }) => (
            <li className={styles.capabilityItem} key={label}>
              <Icon aria-hidden="true" className={styles.capabilityIcon} />
              <span className={styles.capabilityText}>{label}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};
