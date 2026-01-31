import type { ToastServiceMethods } from 'primevue/toastservice';

export function showSuccessToast(toast: ToastServiceMethods, summary: string, detail?: string) {
  toast.add({ severity: 'success', summary, detail, life: 5000 });
}

export function showErrorToast(toast: ToastServiceMethods, summary: string, detail?: string) {
  toast.add({ severity: 'error', summary, detail, life: 5000 });
}

export function showWarnToast(toast: ToastServiceMethods, summary: string, detail?: string) {
  toast.add({ severity: 'warn', summary, detail, life: 5000 });
}
