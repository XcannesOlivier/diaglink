export function formatDate(timestamp: number): string {
  const date = new Date(timestamp * 1000); // Backend sends Unix seconds
  const now = new Date();
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  const conversationDay = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
  const diffDays = Math.round((today - conversationDay) / (1000 * 60 * 60 * 24));

  if (diffDays === 0) return "Aujourd'hui";
  if (diffDays === 1) return 'Hier';
  if (diffDays < 7) return `Il y a ${diffDays} jours`;
  return date.toLocaleDateString('fr-FR');
}
