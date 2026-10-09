import { useCallback, useSyncExternalStore } from 'react';

const minuteListeners = new Set<() => void>();
let minuteInterval: ReturnType<typeof setInterval> | undefined;
let minuteVersion = 0;

const emitMinuteTick = () => {
  minuteVersion += 1;
  minuteListeners.forEach(listener => listener());
};

const subscribeToMinuteTick = (listener: () => void) => {
  minuteListeners.add(listener);
  if (!minuteInterval) {
    minuteInterval = setInterval(emitMinuteTick, 60000);
  }

  return () => {
    minuteListeners.delete(listener);
    if (minuteListeners.size === 0 && minuteInterval) {
      clearInterval(minuteInterval);
      minuteInterval = undefined;
    }
  };
};

const doNotSubscribe = () => () => undefined;
const getMinuteVersion = () => minuteVersion;

/**
 * Hook to format timestamps in a user-friendly way.
 * Returns relative time for recent messages, absolute time for older ones.
 * 
 * @returns A memoized formatter function
 * 
 * @example
 * ```tsx
 * function MessageTimestamp({ timestamp }: { timestamp: Date }) {
 *   const formatTimestamp = useFormatTimestamp();
 *   return <span>{formatTimestamp(timestamp)}</span>;
 * }
 * ```
 * 
 * Examples:
 * - "à l'instant" (< 1 minute)
 * - "il y a 2 minutes"
 * - "il y a 1 heure"
 * - "Hier à 23:50"
 * - "6 oct. à 14:27"
 */
export const useFormatTimestamp = (subscribeToUpdates = true) => {
  useSyncExternalStore(
    subscribeToUpdates ? subscribeToMinuteTick : doNotSubscribe,
    getMinuteVersion,
    getMinuteVersion,
  );

  return useCallback((date: Date | undefined): string => {
    if (!date) {
      return '';
    }

    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMinutes = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);

    const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const startOfMessageDay = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    const startOfYesterday = new Date(startOfToday);
    startOfYesterday.setDate(startOfYesterday.getDate() - 1);

    const time = new Intl.DateTimeFormat('fr-FR', {
      hour: '2-digit',
      minute: '2-digit',
    }).format(date);

    if (startOfMessageDay.getTime() === startOfYesterday.getTime()) {
      return `Hier à ${time}`;
    }

    if (startOfMessageDay.getTime() !== startOfToday.getTime()) {
      const datePart = new Intl.DateTimeFormat('fr-FR', {
        day: 'numeric',
        month: 'short',
        ...(date.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' as const }),
      }).format(date);
      return `${datePart} à ${time}`;
    }

    // Just now (< 1 minute)
    if (diffMinutes < 1) {
      return 'à l\'instant';
    }

    // Minutes ago (< 60 minutes)
    if (diffMinutes < 60) {
      return `il y a ${diffMinutes} minute${diffMinutes === 1 ? '' : 's'}`;
    }

    // Hours ago (same local calendar day)
    return `il y a ${diffHours} heure${diffHours === 1 ? '' : 's'}`;
  }, []);
};
