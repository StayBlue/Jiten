import { defineStore } from 'pinia';
import { DisplayStyle } from '~/types';

export const useDisplayStyleStore = defineStore('displayStyle', () => {
  const displayStyleCookie = useCookie<DisplayStyle>('jiten-display-style', {
    default: () => DisplayStyle.Card,
    watch: true,
    maxAge: 60 * 60 * 24 * 365, // 1 year
    path: '/',
  });

  const displayStyle = ref<DisplayStyle>(displayStyleCookie.value);

  watch(displayStyle, (newValue) => {
    displayStyleCookie.value = newValue;
  });

  return { displayStyle };
});
