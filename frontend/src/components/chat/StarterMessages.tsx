import type { ReactNode } from 'react';
import { Body1, Subtitle1 } from '@fluentui/react-components';
import styles from './StarterMessages.module.css';

interface IStarterMessageProps {
  agentName?: string;
  agentDescription?: string;
  /**
   * Starter prompts from agent metadata.
   * If not provided, falls back to default prompts.
   * 
   * Configure in Microsoft Foundry portal under agent Configuration > Starter prompts.
   * Prompts are stored as newline-separated text in the "starterPrompts" metadata key.
   */
  starterPrompts?: string[];
  onPromptClick?: (prompt: string) => void;
}

// Default starter prompts when none are configured in Microsoft Foundry
const defaultStarterPrompts = [
  "Diagnostiquer un dysfonctionnement",
  "Identifier un composant",
  "Interpréter un schéma technique",
];

export const StarterMessages = ({
  agentName,
  agentDescription,
  starterPrompts,
  onPromptClick,
}: IStarterMessageProps): ReactNode => {
  // Use agent-provided prompts or fall back to defaults
  const prompts = starterPrompts && starterPrompts.length > 0 
    ? starterPrompts 
    : defaultStarterPrompts;
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

      {onPromptClick && (
        <ul className={styles.promptList}>
          {prompts.map((prompt, index) => (
            <li key={`prompt-${index}`}>
              <button
                className={styles.promptCard}
                onClick={() => onPromptClick(prompt)}
                type="button"
                title={prompt}
              >
                <span className={styles.promptText}>{prompt}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};
