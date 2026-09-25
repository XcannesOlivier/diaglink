import { useEffect, useState } from 'react';
import { getApiAuthHeaders } from '../../utils/apiAuth';

export function MachineCreditStatus({ machineId, getAccessToken }: { machineId: string; getAccessToken: () => Promise<string | null> }) {
  const [message, setMessage] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    setMessage('');
    (async () => {
      try {
        const { headers } = await getApiAuthHeaders(getAccessToken);
        const response = await fetch(`${import.meta.env.VITE_API_URL || '/api'}/machines/${encodeURIComponent(machineId)}/ai-credit`, { headers, signal: controller.signal });
        if (!response.ok) throw new Error();
        const data = await response.json();
        if (!controller.signal.aborted) setMessage(data.message);
      } catch {
        if (!controller.signal.aborted) setMessage('');
      }
    })();
    return () => controller.abort();
  }, [machineId, getAccessToken]);
  if (!message) return null;
  return <div><p role="status">{message}</p></div>;
}
