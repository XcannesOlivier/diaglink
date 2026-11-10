import { PrismAsyncLight as SyntaxHighlighter } from 'react-syntax-highlighter';
import vscDarkPlus from 'react-syntax-highlighter/dist/esm/styles/prism/vsc-dark-plus';

interface SyntaxHighlightedCodeProps {
  language: string;
  content: string;
}

export function SyntaxHighlightedCode({ language, content }: SyntaxHighlightedCodeProps) {
  return (
    <SyntaxHighlighter
      language={language}
      style={vscDarkPlus}
      showLineNumbers={true}
      wrapLines={true}
      wrapLongLines={true}
      customStyle={{
        margin: 0,
        borderBottomLeftRadius: '6px',
        borderBottomRightRadius: '6px',
        fontSize: '0.9em',
        maxWidth: '100%',
        overflowX: 'auto',
      }}
      codeTagProps={{
        style: {
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          overflowWrap: 'break-word',
        },
      }}
      PreTag="div"
    >
      {content}
    </SyntaxHighlighter>
  );
}
