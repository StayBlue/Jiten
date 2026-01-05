<script setup lang="ts">
  import OmniSearch from '~/components/OmniSearch.vue';
</script>

<template>
  <div>
    <OmniSearch autofocus />

    <Card class="p-2 mt-4">
      <template #content>
        <h1><b>Welcome to Jiten.moe!</b></h1>
        Jiten is a tool to help you learn Japanese by immersion by providing vocabulary lists and anki decks for all types of media. <br />
        If you have any issues, want to ask questions, make suggestions or just want to chat with the community, feel free to join our
        <a :href="getDiscordLink()" target="_blank">Discord server</a>. <br />
        Jiten is also open source, you can find it on <a href="https://github.com/Sirush/Jiten" target="_blank">Github</a>.<br /><br />

        <div class="flex flex-col gap-4">
          <div>
            <h2><b>Update September 12, 2025</b></h2>
            Accounts are now available! Sign up to get access to coverage, filtering vocabulary lists by words that you know or don't know and downloading decks with only unknown words!
            <br />
            A lot more related features are coming soon :)
          </div>

          <div>
            <h2><b>Update July 29, 2025</b></h2>
            Yomitan frequency dictionaries, better import from JPDB, download occurrences Yomitan dictionaries, bug fixes and improved parser.
          </div>

          <div>
            <h2><b>Update July 21, 2025</b></h2>
            Improved difficulty model, updated JMDict and Sudachi versions, hundreds of new movies, anime and drama. <br />
          </div>

          <div>
            <h2><b>Update June 25, 2025</b></h2>
            New big update with example sentences, deck coverage, and more!
          </div>
        </div>
      </template>
    </Card>
  </div>
</template>

<style scoped></style>
