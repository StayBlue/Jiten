import { LinkType } from '~/types';

export function getLinkTypeText(linkType: LinkType): string {
  switch (linkType) {
    case LinkType.Web:
      return 'Website';
    case LinkType.Tmdb:
      return 'TMDB';
    case LinkType.Anilist:
      return 'Anilist';
    case LinkType.Mal:
      return 'MyAnimeList';
    case LinkType.GoogleBooks:
      return 'Google Books';
    case LinkType.Imdb:
      return 'IMDB';
    case LinkType.Vndb:
      return 'VNDB';
    case LinkType.Igdb:
      return 'IGDB';
    case LinkType.Syosetsu:
      return 'Syosetu';

    default:
      return 'Unknown';
  }
}
