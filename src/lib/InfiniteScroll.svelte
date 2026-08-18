<script lang="ts">
  import { createEventDispatcher, onMount } from "svelte";

  export let threshold = 0;
  export let horizontal = false;
  export let hasMore = true;
  export let elementScroll: HTMLElement | null = null;

  const dispatch = createEventDispatcher<{ loadMore: void }>();
  let isLoadMore = false;
  let marker: HTMLDivElement;

  function onScroll(event?: Event): void {
    const element = elementScroll ?? marker.parentElement;
    if (!element) return;

    const offset = horizontal
      ? element.scrollWidth - element.clientWidth - element.scrollLeft
      : element.scrollHeight - element.clientHeight - element.scrollTop;

    if (offset <= threshold) {
      if (!isLoadMore && hasMore) {
        dispatch("loadMore");
      }
      isLoadMore = true;
    } else {
      isLoadMore = false;
    }
  }

  onMount(() => {
    const element = elementScroll ?? marker.parentElement;
    if (!element) return;

    element.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll, { passive: true });
    onScroll();

    return () => {
      element.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onScroll);
    };
  });
</script>

<div bind:this={marker} class="scroll-marker" aria-hidden="true"></div>

<style>
  .scroll-marker {
    width: 1px;
    height: 1px;
    pointer-events: none;
  }
</style>
