import { Suspense, lazy, memo, useMemo, useCallback, useState } from 'react';
import { Dialog, DialogBody, DialogSurface, DialogTitle, Spinner, Tooltip, Text } from '@fluentui/react-components';
import { CopilotMessage } from '@fluentui-copilot/react-copilot-chat';
import { DocumentRegular, GlobeRegular, OpenRegular, ArrowSyncRegular } from '@fluentui/react-icons';
import { MessageActions } from './MessageActions';
import { TechnicalVisualGallery } from './TechnicalVisualGallery';
import { DialogCloseButton } from '../core/DialogCloseButton';
import { useFormatTimestamp } from '../../hooks/useFormatTimestamp';
import { parseContentWithCitations } from '../../utils/citationParser';
import { compactVerifiedSourceLines } from '../../utils/technicalSources';
import type { IChatItem, IAnnotation, TechnicalSourceReference } from '../../types/chat';
import styles from './AssistantMessage.module.css';

const TechnicalSourcePdfDialog = lazy(() => import('./TechnicalSourcePdfDialog').then(module => ({
  default: module.TechnicalSourcePdfDialog,
})));

const Markdown = lazy(() => import('../core/Markdown').then(module => ({
  default: module.Markdown,
})));

function getToolUseLabel(toolName: string): string {
  switch (toolName) {
    case 'file_search':
      return 'Recherche de fichiers\u2026';
    case 'code_interpreter':
      return 'Exécution du code\u2026';
    case 'function_call':
      return "Appel de l'outil\u2026";
    default:
      return 'Traitement\u2026';
  }
}

interface AssistantMessageProps {
  message: IChatItem;
  agentName?: string;
  isStreaming?: boolean;
  disabled?: boolean;
  onRegenerate?: () => void;
  onFeedback?: (messageId: string, rating: 'positive' | 'negative') => void;
  onLoadTechnicalVisual?: (visualId: number, signal?: AbortSignal) => Promise<Blob>;
  onLoadTechnicalSource?: (sourceReferenceId: number, signal?: AbortSignal) => Promise<Blob>;
  onSuggestedPromptClick?: (prompt: string) => void;
}

function AssistantMessageComponent({ 
  message, 
  agentName = 'AI Assistant',
  isStreaming = false,
  disabled = false,
  onRegenerate,
  onLoadTechnicalVisual,
  onLoadTechnicalSource,
  onSuggestedPromptClick,
}: AssistantMessageProps) {
  const formatTimestamp = useFormatTimestamp(!!message.more?.time);
  const [selectedSource, setSelectedSource] = useState<TechnicalSourceReference>();
  const timestamp = message.more?.time ? formatTimestamp(new Date(message.more.time)) : '';
  
  // Show custom loading indicator when streaming with no content
  const showLoadingDots = isStreaming && !message.content && !message.retryAttempt;
  const isRetrying = isStreaming && !!message.retryAttempt;
  const hasAnnotations = message.annotations && message.annotations.length > 0;

  const webCitations = useMemo(() => {
    const citationsByUrl = new Map<string, IAnnotation>();
    message.annotations?.forEach((annotation) => {
      if (annotation.type === 'uri_citation' && annotation.url && !citationsByUrl.has(annotation.url)) {
        citationsByUrl.set(annotation.url, annotation);
      }
    });
    return Array.from(citationsByUrl.values());
  }, [message.annotations]);

  const technicalSourceGroups = useMemo(() => {
    const groups = new Map<string, { documentName: string; pages: TechnicalSourceReference[] }>();
    message.sources?.forEach((source) => {
      const documentName = source.documentName || 'Document constructeur';
      let group = groups.get(documentName);
      if (!group) {
        group = { documentName, pages: [] };
        groups.set(documentName, group);
      }
      const samePhysicalPage = group.pages.findIndex(page => page.pdfPage === source.pdfPage);
      if (samePhysicalPage < 0) {
        group.pages.push(source);
      } else if (
        group.pages[samePhysicalPage].displayPage === String(source.pdfPage) &&
        source.displayPage !== String(source.pdfPage)
      ) {
        // Prefer the printed manual number when two verified labels target the same PDF page.
        group.pages[samePhysicalPage] = source;
      }
    });
    return Array.from(groups.values());
  }, [message.sources]);

  const presentedTechnicalContent = useMemo(
    () => compactVerifiedSourceLines(message.content, message.sources),
    [message.content, message.sources],
  );
  
  // Parse content with citations for consistent numbering between inline and footnotes
  const parsedContent = useMemo(() => {
    if (!hasAnnotations) return null;
    return parseContentWithCitations(message.content, message.annotations);
  }, [message.content, message.annotations, hasAnnotations]);

  const suggestedPrompts = isStreaming ? [] : (message.suggestions ?? []);

  // Get unique annotations with consistent indices
  // If the parser found citations (inline placeholders), use those
  // Otherwise, fall back to displaying all annotations as footnotes
  const indexedCitations = useMemo(() => {
    if (parsedContent?.citations && parsedContent.citations.length > 0) {
      return parsedContent.citations;
    }
    // No inline placeholders found - display all annotations as numbered footnotes
    // Deduplicate by label+type for fallback case
    if (message.annotations && message.annotations.length > 0) {
      const seen = new Map<string, { index: number; annotation: IAnnotation; count: number }>();
      message.annotations.forEach((annotation) => {
        const key = `${annotation.type}:${annotation.label}:${annotation.url || annotation.fileId || ''}`;
        if (seen.has(key)) {
          seen.get(key)!.count++;
        } else {
          seen.set(key, { index: seen.size + 1, annotation, count: 1 });
        }
      });
      return Array.from(seen.values());
    }
    return [];
  }, [parsedContent, message.annotations]);
  
  // Handle citation click - scroll to footnote or open URL
  const handleCitationClick = useCallback((index: number, annotation?: IAnnotation) => {
    if (annotation?.type === 'uri_citation' && annotation.url) {
      window.open(annotation.url, '_blank', 'noopener,noreferrer');
    } else {
      // Scroll to citation in footnotes
      const citationElement = document.getElementById(`citation-${message.id}-${index}`);
      if (citationElement) {
        citationElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
        citationElement.classList.add(styles.citationHighlight);
        setTimeout(() => {
          citationElement.classList.remove(styles.citationHighlight);
        }, 2000);
      }
    }
  }, [message.id]);
  
  // Build citation elements matching Foundry style
  const renderCitation = (annotation: IAnnotation, index: number, count: number = 1) => {
    const getIcon = () => {
      switch (annotation.type) {
        case 'uri_citation':
          return <GlobeRegular className={styles.citationIcon} />;
        default:
          return <DocumentRegular className={styles.citationIcon} />;
      }
    };

    const citationNumber = index;
    const tooltipContent = annotation.quote 
      ? `${annotation.label}${count > 1 ? ` (référencé ${count} fois)` : ''}\n\n"${annotation.quote.slice(0, 200)}${annotation.quote.length > 200 ? '...' : ''}"`
      : `${annotation.label}${count > 1 ? ` (référencé ${count} fois)` : ''}`;

    const isClickable = annotation.type === 'uri_citation' && !!annotation.url;

    const handleClick = () => {
      if (annotation.type === 'uri_citation' && annotation.url) {
        window.open(annotation.url, '_blank', 'noopener,noreferrer');
      }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
      if (isClickable && (e.key === 'Enter' || e.key === ' ')) {
        e.preventDefault();
        handleClick();
      }
    };

    // Render citation button matching Foundry style
    return (
      <Tooltip
        key={`${annotation.label}-${index}`}
        content={tooltipContent}
        relationship="description"
        withArrow
      >
        <span 
          id={`citation-${message.id}-${citationNumber}`}
          className={`${styles.citation} ${isClickable ? styles.citationClickable : ''}`}
          onClick={isClickable ? handleClick : undefined}
          onKeyDown={isClickable ? handleKeyDown : undefined}
          role={isClickable ? 'button' : undefined}
          aria-label={isClickable ? `Ouvrir ${annotation.label}` : undefined}
          tabIndex={isClickable ? 0 : undefined}
        >
          <span className={styles.citationNumber}>{citationNumber}</span>
          <span className={styles.citationContent}>
            {getIcon()}
            <span className={styles.citationLabel}>{annotation.label}</span>
            {count > 1 && <span className={styles.citationCount}>×{count}</span>}
            {isClickable && <OpenRegular className={styles.citationExternalIcon} />}
          </span>
        </span>
      </Tooltip>
    );
  };

  const citations = indexedCitations
    .filter(({ annotation }) => annotation.type !== 'uri_citation' || !annotation.url)
    .map(({ index, annotation, count }) => renderCitation(annotation, index, count));
  
  return (
    <CopilotMessage
      id={`msg-${message.id}`}
      avatar={<div aria-hidden="true" className={styles.hiddenAvatar} />}
      name={agentName}
      loadingState="none"
      className={styles.copilotMessage}
      disclaimer={null}
      footnote={
        <div className={styles.footnoteContainer}>
          {citations.length > 0 && !isStreaming && (
            <div className={styles.citationList}>
              {citations}
            </div>
          )}
          <div className={styles.metadataRow}>
            <div className={styles.metadataLeft}>
              {timestamp && <span className={styles.timestamp}>{timestamp}</span>}
            </div>
            {!isStreaming && message.content && onRegenerate && (
              <MessageActions
                content={message.content}
                onRegenerate={onRegenerate}
              />
            )}
          </div>
        </div>
      }
    >
      {showLoadingDots ? (
        isStreaming && message.activeToolUse ? (
          <div className={styles.toolUseIndicator}>
            <Spinner size="tiny" />
            <Text size={200}>{getToolUseLabel(message.activeToolUse)}</Text>
          </div>
        ) : (
          <div className={styles.loadingDots} role="status" aria-label="L'assistant réfléchit">
            <span></span>
            <span></span>
            <span></span>
          </div>
        )
      ) : isRetrying ? (
        <div className={styles.retryingState}>
          <ArrowSyncRegular className={styles.retryingIcon} />
          <Text size={200}>
            Nouvelle tentative ({message.retryAttempt}/{message.maxRetries})...
          </Text>
        </div>
      ) : (
        <>
          <Suspense fallback={<Spinner size="small" />}>
            <Markdown
              content={isStreaming ? message.content : presentedTechnicalContent.content}
              annotations={message.annotations}
              onCitationClick={handleCitationClick}
              sources={isStreaming ? message.sources : presentedTechnicalContent.sources}
              onTechnicalSourceClick={onLoadTechnicalSource ? setSelectedSource : undefined}
            />
          </Suspense>
          {isStreaming && message.activeToolUse && (
            <div className={styles.toolUseIndicator} role="status" aria-label={getToolUseLabel(message.activeToolUse)}>
              <Spinner size="tiny" />
              <Text size={200}>{getToolUseLabel(message.activeToolUse)}</Text>
            </div>
          )}
          {!isStreaming && technicalSourceGroups.length > 0 && (
            <section
              className={styles.technicalSources}
              aria-label="Sources documentaires"
            >
              <DocumentRegular className={styles.technicalSourcesIcon} aria-hidden="true" />
              <span className={styles.technicalSourcesLabel}>Sources :</span>
              <ul className={styles.technicalSourceList}>
                {technicalSourceGroups.map(group => (
                  <li key={group.documentName} className={styles.technicalSourceDocument}>
                    <span className={styles.technicalSourceName} title={group.documentName}>
                      {group.documentName}
                    </span>
                    {group.pages.map((source, index) => (
                      <span key={`${source.id}-${source.pdfPage}`} className={styles.technicalSourcePageItem}>
                        {index > 0 && <span aria-hidden="true">,</span>}
                        <button
                          type="button"
                          className={styles.technicalSourcePage}
                          onClick={() => setSelectedSource(source)}
                          disabled={!onLoadTechnicalSource}
                          aria-label={`Ouvrir ${group.documentName}, page ${source.displayPage}`}
                        >
                          <span>p. {source.displayPage}</span>
                          <OpenRegular aria-hidden="true" />
                        </button>
                      </span>
                    ))}
                  </li>
                ))}
              </ul>
            </section>
          )}
          {message.visuals && message.visuals.length > 0 && onLoadTechnicalVisual && (
            <TechnicalVisualGallery visuals={message.visuals} loadVisual={onLoadTechnicalVisual} />
          )}
          {!isStreaming && webCitations.length > 0 && (
            <section
              className={styles.webSources}
              aria-labelledby={`web-sources-${message.id}`}
            >
              <h3 id={`web-sources-${message.id}`} className={styles.webSourcesTitle}>
                Sources Web
              </h3>
              <ul className={styles.webSourceList}>
                {webCitations.map((citation) => (
                  <li key={citation.url} className={styles.webSourceItem}>
                    <a
                      className={styles.webSourceLink}
                      href={citation.url}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      <GlobeRegular aria-hidden="true" />
                      <span>{citation.label || citation.url}</span>
                      <OpenRegular aria-hidden="true" />
                    </a>
                    {citation.quote && (
                      <details className={styles.webSourceDetails}>
                        <summary className={styles.webSourceSummary}>Voir l’extrait</summary>
                        <p className={styles.webSourceQuote}>{citation.quote}</p>
                      </details>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          )}
          {suggestedPrompts.length > 0 && onSuggestedPromptClick && (
            <div className={styles.suggestedPrompts}>
              {suggestedPrompts.map((prompt, index) => (
                <button
                  key={`suggested-${message.id}-${index}`}
                  type="button"
                  className={styles.suggestedPromptChip}
                  onClick={() => onSuggestedPromptClick(prompt)}
                  disabled={disabled}
                >
                  {prompt}
                </button>
              ))}
            </div>
          )}
          {selectedSource && onLoadTechnicalSource && (
            <Suspense fallback={(
              <Dialog open onOpenChange={(_event, data) => !data.open && setSelectedSource(undefined)}>
                <DialogSurface aria-label="Ouverture du document source">
                  <DialogBody>
                    <DialogTitle action={<DialogCloseButton onClick={() => setSelectedSource(undefined)} />}>
                      Ouverture du document
                    </DialogTitle>
                    <Spinner label="Préparation du visualiseur PDF…" />
                  </DialogBody>
                </DialogSurface>
              </Dialog>
            )}>
              <TechnicalSourcePdfDialog
                source={selectedSource}
                loadPdf={onLoadTechnicalSource}
                onClose={() => setSelectedSource(undefined)}
              />
            </Suspense>
          )}
        </>
      )}
    </CopilotMessage>
  );
}

export const AssistantMessage = memo(AssistantMessageComponent, (prev, next) => {
  return (
    prev.message.id === next.message.id &&
    prev.message.content === next.message.content &&
    prev.isStreaming === next.isStreaming &&
    prev.disabled === next.disabled &&
    prev.message.more?.usage === next.message.more?.usage &&
    prev.message.annotations?.length === next.message.annotations?.length &&
    prev.message.visuals === next.message.visuals &&
    prev.message.sources === next.message.sources &&
    prev.message.suggestions === next.message.suggestions &&
    prev.onLoadTechnicalVisual === next.onLoadTechnicalVisual &&
    prev.onLoadTechnicalSource === next.onLoadTechnicalSource &&
    prev.message.retryAttempt === next.message.retryAttempt &&
    prev.message.activeToolUse === next.message.activeToolUse
  );
});
