import { DeckDownloadType } from '~/types';

export function getDownloadTypeText(downloadType: DeckDownloadType): string {
  switch (downloadType) {
    case DeckDownloadType.Full:
      return 'Full Deck';
    case DeckDownloadType.TopGlobalFrequency:
      return 'Top Global Frequency';
    case DeckDownloadType.TopDeckFrequency:
      return 'Top Deck Frequency';
    case DeckDownloadType.TopChronological:
      return 'Top Chronological';
    default:
      return 'Unknown';
  }
}
