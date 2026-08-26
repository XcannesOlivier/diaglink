import React from 'react';
import { Button, Text } from '@fluentui/react-components';
import { CopilotMessage } from '@fluentui-copilot/react-copilot-chat';
import { AgentIcon } from '../core/AgentIcon';
import styles from './McpApprovalCard.module.css';

interface McpApprovalCardProps {
  toolName: string;
  serverLabel: string;
  arguments?: string;
  onApprove: () => void;
  onReject: () => void;
  disabled?: boolean;
  resolved?: 'approved' | 'rejected';
  agentName?: string;
  agentLogo?: string;
}

export const McpApprovalCard: React.FC<McpApprovalCardProps> = ({
  toolName,
  serverLabel,
  arguments: args,
  onApprove,
  onReject,
  disabled,
  resolved,
  agentName = 'AI Assistant',
  agentLogo,
}) => {
  return (
    <CopilotMessage
      avatar={<AgentIcon logoUrl={agentLogo} />}
      name={agentName}
      loadingState="none"
      className={styles.message}
    >
      <div className={styles.content}>
        <Text className={styles.title}>
          {resolved
            ? `Outil externe ${resolved === 'approved' ? 'approuvé' : 'refusé'}`
            : "J'ai besoin de votre approbation pour utiliser un outil externe"}
        </Text>
        
        <div className={styles.details}>
          <div className={styles.detail}>
            <Text weight="semibold">Outil :</Text> <Text>{toolName}</Text>
          </div>
          <div className={styles.detail}>
            <Text weight="semibold">Serveur :</Text> <Text>{serverLabel}</Text>
          </div>
          {args && (
            <details className={styles.argumentsDisclosure}>
              <summary className={styles.argumentsSummary}>Voir les arguments</summary>
              <pre className={styles.arguments}>{(() => {
                try {
                  return JSON.stringify(JSON.parse(args), null, 2);
                } catch {
                  return args;
                }
              })()}</pre>
            </details>
          )}
        </div>
        
        {resolved ? (
          <div className={styles.resolvedStatus}>
            <Text weight="semibold">
              {resolved === 'approved' ? '✓ Approuvé' : '✗ Refusé'}
            </Text>
          </div>
        ) : (
          <div className={styles.actions}>
            <Button 
              appearance="primary"
              onClick={onApprove} 
              disabled={disabled}
            >
              Approuver
            </Button>
            <Button 
              appearance="secondary"
              onClick={onReject} 
              disabled={disabled}
            >
              Refuser
            </Button>
          </div>
        )}
      </div>
    </CopilotMessage>
  );
};
