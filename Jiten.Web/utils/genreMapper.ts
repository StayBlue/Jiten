import { Genre } from '~/types/enums';

export function getGenreText(genre: Genre): string {
  const mapping: Record<Genre, string> = {
    [Genre.Action]: 'Action',
    [Genre.Adventure]: 'Adventure',
    [Genre.Comedy]: 'Comedy',
    [Genre.Drama]: 'Drama',
    [Genre.Ecchi]: 'Ecchi',
    [Genre.Fantasy]: 'Fantasy',
    [Genre.Horror]: 'Horror',
    [Genre.Mecha]: 'Mecha',
    [Genre.Music]: 'Music',
    [Genre.Mystery]: 'Mystery',
    [Genre.Psychological]: 'Psychological',
    [Genre.Romance]: 'Romance',
    [Genre.SciFi]: 'Sci-Fi',
    [Genre.SliceOfLife]: 'Slice of Life',
    [Genre.Sports]: 'Sports',
    [Genre.Supernatural]: 'Supernatural',
    [Genre.Thriller]: 'Thriller',
    [Genre.AdultOnly]: 'Adult Only',
  };
  return mapping[genre] || 'Unknown';
}

export function getAllGenres(): { value: Genre; label: string }[] {
  return Object.values(Genre)
    .filter((v) => typeof v === 'number')
    .map((v) => ({
      value: v as Genre,
      label: getGenreText(v as Genre),
    }));
}
