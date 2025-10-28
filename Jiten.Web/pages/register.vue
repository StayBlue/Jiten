<script setup lang="ts">
  const { $api } = useNuxtApp();

  const form = reactive({
    username: '',
    email: '',
    password: '',
    tosAccepted: false,
    receiveNewsletter: false,
  });

  const recaptchaResponse = ref();
  useRecaptchaProvider();

  const isLoading = ref(false);
  const message = ref<string | null>(null);
  const error = ref<string | null>(null);

  async function handleRegister() {
    error.value = null;
    message.value = null;
    isLoading.value = true;
    try {
      if (!recaptchaResponse.value) {
        throw new Error('Please complete the reCAPTCHA.');
      }
      await $api('/auth/register', { method: 'POST', body: { ...form, recaptchaResponse: recaptchaResponse.value } });
      message.value =
        "Registration successful. Please check your email to confirm your account. If you don't receive the email within a few minutes, please contact us on Discord or send an email to contact@jiten.moe from the email address you used to register for a manual confirmation.";
    } catch (err: any) {
      if (err.response && err.response._data) {
        const apiMessage = err.response._data.message || 'Registration failed.';
        error.value = `Registration failed: ${apiMessage}`;
      } else {
        error.value = (err as Error)?.message ? `Registration failed: ${(err as Error).message}` : 'Registration failed: An unexpected error occurred.';
      }
    } finally {
      isLoading.value = false;
    }
  }
</script>

<template>
  <Card class="max-w-120 mx-auto p-2">
    <template #title>Register</template>
    <template #content>
      <form @submit.prevent="handleRegister" class="flex flex-col gap-6 pt-4">
        <div class="w-full">
          <FloatLabel>
            <InputText id="username" v-model.trim="form.username" required class="w-full" />
            <label for="username">Username</label>
          </FloatLabel>
        </div>
        <div class="w-full">
          <FloatLabel>
            <InputText id="email" v-model.trim="form.email" type="email" required class="w-full" />
            <label for="email">Email</label>
          </FloatLabel>
        </div>
        <div class="w-full">
          <FloatLabel>
            <Password
              id="password"
              v-model="form.password"
              toggleMask
              :feedback="true"
              :promptLabel="'At least 10 characters including upper, lower, digit'"
              :weakLabel="'Weak'"
              :mediumLabel="'Medium'"
              :strongLabel="'Strong'"
              :inputProps="{ autocomplete: 'new-password', minlength: 10 }"
              :inputClass="'w-full'"
              required
            />
            <label for="password">Password</label>
          </FloatLabel>
        </div>

        <div class="flex flex-col gap-4 pt-2">
          <div class="flex items-start gap-3">
            <Checkbox inputId="terms" v-model="form.tosAccepted" name="terms" binary required class="mt-1" />
            <label for="terms" class="text-sm text-gray-700 leading-relaxed cursor-pointer">
              I agree to the
              <NuxtLink to="/terms" target="_blank" class="text-blue-600 hover:text-blue-800 hover:underline font-medium"> Terms of Service </NuxtLink>
              and
              <NuxtLink to="/privacy" target="_blank" class="text-blue-600 hover:text-blue-800 hover:underline font-medium"> Privacy Policy </NuxtLink>
            </label>
          </div>

          <div class="flex items-start gap-3">
            <Checkbox inputId="newsletter" v-model="form.receiveNewsletter" name="newsletter" binary class="mt-1" />
            <label for="newsletter" class="text-sm text-gray-700 leading-relaxed cursor-pointer">
              I would like to receive occasional updates and newsletters via email
            </label>
          </div>
        </div>

        <RecaptchaCheckbox v-model="recaptchaResponse" class="my-2" />
        <Button type="submit" :disabled="isLoading" class="w-full">{{ isLoading ? 'Registering...' : 'Register' }}</Button>
      </form>
      <p v-if="message" class="text-amber-400">{{ message }}</p>
      <p v-if="error" class="text-red-500">{{ error }}</p>
      <div class="links">
        <NuxtLink to="/login">Back to Login</NuxtLink>
      </div>
    </template>
  </Card>
</template>

<style scoped>
  /* Ensure PrimeVue Password component takes full width */
  :deep(.p-password) {
    width: 100%;
  }

  :deep(.p-password input) {
    width: 100%;
  }

  /* Ensure consistent input heights */
  :deep(.p-inputtext),
  :deep(.p-password input) {
    min-height: 3rem;
  }

  /* Better spacing for float labels */
  :deep(.p-float-label) {
    margin-bottom: 0;
  }

  /* Improve checkbox alignment */
  :deep(.p-checkbox) {
    flex-shrink: 0;
  }
</style>
