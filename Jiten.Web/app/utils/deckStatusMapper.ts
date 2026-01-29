import { DeckStatus } from '~/types';

export function getDeckStatusText(status: DeckStatus | undefined): string {
  if (status === undefined || status === DeckStatus.None) return 'None';

  switch (status) {
    case DeckStatus.Planning:
      return 'Planning';
    case DeckStatus.Ongoing:
      return 'Ongoing';
    case DeckStatus.Completed:
      return 'Completed';
    case DeckStatus.Dropped:
      return 'Dropped';
    default:
      return 'Unknown';
  }
}
