import React, { useCallback, useState, useRef, useMemo, useEffect } from 'react';
import {
  Drawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  Spinner,
  Text,
  Input,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  ChatAdd24Regular,
  Delete24Regular,
  Search24Regular,
  DismissCircle24Regular,
} from '@fluentui/react-icons';
import type { ConversationSummary } from '../types/appState';

interface ConversationSidebarProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  conversations: ConversationSummary[];
  isLoading: boolean;
  hasMore: boolean;
  currentConversationId: string | null;
  onSelectConversation: (conversationId: string) => void;
  onNewChat: () => void;
  onDeleteConversation: (conversationId: string) => void;
  onLoadMore: () => void;
  onCollapse: () => void;
  canCollapse: boolean;
}

const useStyles = makeStyles({
  drawer: {
    width: '320px',
  },
  newChatButton: {
    width: '100%',
    marginBottom: tokens.spacingVerticalM,
  },
  conversationList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  conversationItem: {
    display: 'flex',
    alignItems: 'center',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    border: 'none',
    backgroundColor: 'transparent',
    width: '100%',
    textAlign: 'left',
    gap: tokens.spacingHorizontalS,
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  conversationItemActive: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
  },
  conversationContent: {
    flex: 1,
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
  },
  conversationTitle: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  conversationDate: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  deleteButton: {
    flexShrink: 0,
    opacity: 0,
    '.conversation-item:hover &, .conversation-item:focus-within &': {
      opacity: 1,
    },
    ':focus': {
      opacity: 1,
    },
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  spinnerContainer: {
    display: 'flex',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
  },
  loadMoreButton: {
    width: '100%',
    marginTop: tokens.spacingVerticalS,
  },
  searchBox: {
    marginBottom: tokens.spacingVerticalS,
  },
  noResults: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

function formatDate(timestamp: number): string {
  const date = new Date(timestamp * 1000); // Backend sends Unix seconds
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

  if (diffDays === 0) return "Aujourd'hui";
  if (diffDays === 1) return 'Hier';
  if (diffDays < 7) return `Il y a ${diffDays} jours`;
  return date.toLocaleDateString();
}

export const ConversationSidebar: React.FC<ConversationSidebarProps> = ({
  isOpen,
  onOpenChange,
  conversations,
  isLoading,
  hasMore,
  currentConversationId,
  onSelectConversation,
  onNewChat,
  onDeleteConversation,
  onLoadMore,
  onCollapse,
  canCollapse,
}) => {
  const styles = useStyles();
  const [searchQuery, setSearchQuery] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [debouncedQuery, setDebouncedQuery] = useState('');

  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const handleSearchChange = useCallback((_: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
    setSearchQuery(data.value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(data.value);
    }, 300);
  }, []);

  const handleClearSearch = useCallback(() => {
    setSearchQuery('');
    setDebouncedQuery('');
    if (debounceRef.current) clearTimeout(debounceRef.current);
  }, []);

  const filteredConversations = useMemo(() => {
    if (!debouncedQuery.trim()) return conversations;
    const query = debouncedQuery.toLowerCase();
    return conversations.filter(c => c.title?.toLowerCase().includes(query));
  }, [conversations, debouncedQuery]);

  const handleDelete = useCallback(
    (e: React.MouseEvent, conversationId: string) => {
      e.stopPropagation();
      onDeleteConversation(conversationId);
    },
    [onDeleteConversation]
  );

  return (
    <Drawer
      open={isOpen}
      onOpenChange={(_, { open }) => onOpenChange(open)}
      position="start"
      className={styles.drawer}
    >
      <DrawerHeader>
        <DrawerHeaderTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Fermer le panneau"
              icon={<Dismiss24Regular />}
              onClick={() => onOpenChange(false)}
            />
          }
        >
          Conversations
        </DrawerHeaderTitle>
      </DrawerHeader>

      <DrawerBody>
        <Button
          appearance="primary"
          icon={<ChatAdd24Regular />}
          className={styles.newChatButton}
          onClick={() => {
            onNewChat();
            onOpenChange(false);
          }}
        >
          Nouvelle conversation
        </Button>

        {conversations.length > 0 && (
          <Input
            className={styles.searchBox}
            placeholder="Rechercher des conversations…"
            value={searchQuery}
            onChange={handleSearchChange}
            contentBefore={<Search24Regular />}
            contentAfter={
              searchQuery ? (
                <Button
                  appearance="transparent"
                  icon={<DismissCircle24Regular />}
                  size="small"
                  aria-label="Effacer la recherche"
                  onClick={handleClearSearch}
                />
              ) : undefined
            }
            aria-label="Rechercher des conversations"
          />
        )}

        {isLoading && conversations.length === 0 ? (
          <div className={styles.spinnerContainer}>
            <Spinner size="small" label="Chargement des conversations…" />
          </div>
        ) : conversations.length === 0 ? (
          <div className={styles.emptyState}>
            <Text>Aucune conversation pour le moment</Text>
            <Text size={200}>Démarrez une nouvelle conversation pour commencer</Text>
          </div>
        ) : filteredConversations.length === 0 ? (
          <div className={styles.noResults}>
            <Text>Aucune conversation ne correspond</Text>
            <Text size={200}>Essayez un autre terme de recherche</Text>
          </div>
        ) : (
          <>
            <div className={styles.conversationList} role="list">
              {filteredConversations.map((conversation) => (
                <div
                  key={conversation.id}
                  className={`conversation-item ${styles.conversationItem} ${
                    conversation.id === currentConversationId
                      ? styles.conversationItemActive
                      : ''
                  }`}
                  role="listitem"
                  onClick={() => {
                    onSelectConversation(conversation.id);
                    onOpenChange(false);
                  }}
                  tabIndex={0}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      onSelectConversation(conversation.id);
                      onOpenChange(false);
                    }
                  }}
                >
                  <div className={styles.conversationContent}>
                    <Text
                      weight="semibold"
                      size={300}
                      className={styles.conversationTitle}
                    >
                      {conversation.title || 'Sans titre'}
                    </Text>
                    <Text className={styles.conversationDate}>
                      {formatDate(conversation.createdAt)}
                    </Text>
                  </div>
                  <Button
                    appearance="subtle"
                    icon={<Delete24Regular />}
                    size="small"
                    className={styles.deleteButton}
                    aria-label={`Supprimer la conversation : ${conversation.title || 'Sans titre'}`}
                    onClick={(e) => handleDelete(e, conversation.id)}
                  />
                </div>
              ))}
            </div>
            {hasMore && (
              <Button
                appearance="subtle"
                className={styles.loadMoreButton}
                onClick={onLoadMore}
                disabled={isLoading}
              >
                {isLoading ? 'Chargement…' : 'Charger plus de conversations'}
              </Button>
            )}
            {canCollapse && (
              <Button
                appearance="subtle"
                className={styles.loadMoreButton}
                onClick={onCollapse}
                disabled={isLoading}
              >
                Afficher moins
              </Button>
            )}
          </>
        )}
      </DrawerBody>
    </Drawer>
  );
};
