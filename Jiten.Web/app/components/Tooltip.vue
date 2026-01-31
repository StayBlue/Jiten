<template>
  <div ref="referenceRef" :class="block ? 'block' : 'inline-block'">
    <slot :toggle="toggle" :show="show" :hide="hide" />
  </div>

  <Teleport to="body" v-if="formattedContent != ''">
    <Transition
      enter-active-class="transition-opacity duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition-opacity duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="isVisible"
        ref="floatingRef"
        :style="{
          position: strategy,
          top: `${y ?? 0}px`,
          left: `${x ?? 0}px`,
        }"
        class="z-50"
      >
        <div class="bg-gray-900 dark:bg-gray-800 text-white px-3 py-2 rounded-lg shadow-lg text-sm max-w-sm">
          <div v-html="formattedContent" class="whitespace-pre-wrap" />
        </div>

        <!-- Arrow -->
        <div
          ref="arrowRef"
          :style="{
            position: 'absolute',
            left: arrowX != null ? `${arrowX}px` : '',
            top: arrowY != null ? `${arrowY}px` : '',
            ...arrowStyle,
          }"
          class="w-0 h-0"
        >
          <div class="absolute border-transparent" :class="arrowClasses" />
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
  import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
  import { useFloating, autoUpdate, offset, flip, shift, arrow } from '@floating-ui/vue';

  interface Props {
    content: string;
    placement?: 'top' | 'bottom' | 'left' | 'right';
    offset?: number;
    block?: boolean;
  }

  const props = withDefaults(defineProps<Props>(), {
    placement: 'top',
    offset: 8,
  });

  const referenceRef = ref<HTMLElement | null>(null);
  const floatingRef = ref<HTMLElement | null>(null);
  const arrowRef = ref<HTMLElement | null>(null);
  const isVisible = ref(false);
  const isMobile = ref(false);

  // Format content with basic HTML support
  const formattedContent = computed(() => {
    return props.content.replace(/\*\*(.*?)\*\*/g, '<strong class="font-semibold">$1</strong>').replace(/\n/g, '<br>');
  });

  // Floating UI setup
  const {
    x,
    y,
    strategy,
    middlewareData,
    placement: computedPlacement,
  } = useFloating(referenceRef, floatingRef, {
    placement: props.placement,
    middleware: [offset(props.offset), flip(), shift({ padding: 8 }), arrow({ element: arrowRef })],
    whileElementsMounted: autoUpdate,
  });

  // Arrow positioning
  const arrowX = computed(() => middlewareData.value.arrow?.x);
  const arrowY = computed(() => middlewareData.value.arrow?.y);

  const arrowStyle = computed(() => {
    const side = computedPlacement.value.split('-')[0]
    switch (side) {
      case 'top':
        return { bottom: '-6px' }
      case 'bottom':
        return { top: '-6px' }
      case 'left':
        return { right: '-6px' }
      case 'right':
        return { left: '-6px' }
      default:
        return {}
    }
  })

  const arrowClasses = computed(() => {
    const side = computedPlacement.value.split('-')[0]
    const baseColor = 'border-gray-900 dark:border-gray-800'

    switch (side) {
      case 'bottom':
        return `${baseColor} border-b-8 border-x-8 border-x-transparent border-b-gray-900 dark:border-b-gray-800 top-0`
      case 'top':
        return `${baseColor} border-t-8 border-x-8 border-x-transparent border-t-gray-900 dark:border-t-gray-800 bottom-0`
      case 'right':
        return `${baseColor} border-r-8 border-y-8 border-y-transparent border-r-gray-900 dark:border-r-gray-800 left-0`
      case 'left':
        return `${baseColor} border-l-8 border-y-8 border-y-transparent border-l-gray-900 dark:border-l-gray-800 right-0`
      default:
        return ''
    }
  })

  // Show/hide functions
  const show = () => {
    isVisible.value = true;
  };

  const hide = () => {
    isVisible.value = false;
  };

  const toggle = () => {
    isVisible.value = !isVisible.value;
  };

  // Event handlers
  const handleMouseEnter = () => {
    if (!isMobile.value) show();
  };

  const handleMouseLeave = () => {
    if (!isMobile.value) hide();
  };

  const handleClick = (e: Event) => {
    if (isMobile.value) {
      e.stopPropagation();
      toggle();
    }
  };

  const handleClickOutside = (e: Event) => {
    if (
      isMobile.value &&
      isVisible.value &&
      referenceRef.value &&
      floatingRef.value &&
      !referenceRef.value.contains(e.target as Node) &&
      !floatingRef.value.contains(e.target as Node)
    ) {
      hide();
    }
  };

  // Detect mobile
  const checkMobile = () => {
    isMobile.value = 'ontouchstart' in window || navigator.maxTouchPoints > 0;
  };

  onMounted(() => {
    checkMobile();

    if (referenceRef.value) {
      referenceRef.value.addEventListener('mouseenter', handleMouseEnter);
      referenceRef.value.addEventListener('mouseleave', handleMouseLeave);
      referenceRef.value.addEventListener('click', handleClick);
    }

    document.addEventListener('click', handleClickOutside);
  });

  onBeforeUnmount(() => {
    if (referenceRef.value) {
      referenceRef.value.removeEventListener('mouseenter', handleMouseEnter);
      referenceRef.value.removeEventListener('mouseleave', handleMouseLeave);
      referenceRef.value.removeEventListener('click', handleClick);
    }

    document.removeEventListener('click', handleClickOutside);
  });

  defineExpose({ show, hide, toggle });
</script>
