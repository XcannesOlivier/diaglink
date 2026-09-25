import React from 'react';
import { Caption1Strong } from '@fluentui/react-components';
import styles from './BuiltWithBadge.module.css';

interface BuiltWithBadgeProps {
  className?: string;
}

export const BuiltWithBadge: React.FC<BuiltWithBadgeProps> = ({ className }) => {
  const handleClick = () => {
    // Link to Microsoft Foundry marketing page
    // In production, this could fetch user's Azure config and link to their specific project
    window.open('https://azure.microsoft.com/en-us/products/ai-foundry', '_blank');
  };

  return (
    <button
      className={`${styles.badge} ${className || ''}`}
      onClick={handleClick}
      type="button"
      aria-label="Créé avec Microsoft Foundry"
    >
      <Caption1Strong className={`${styles.text} ${styles.poweredByText}`}>
        Propulsé par
      </Caption1Strong>
      <Caption1Strong className={styles.brand}>
        Microsoft Foundry
      </Caption1Strong>
    </button>
  );
};
