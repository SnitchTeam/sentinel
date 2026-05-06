<script lang="ts">
  interface Props {
    onLogout?: () => void
  }

  let { onLogout }: Props = $props()
  let open = $state(false)

  const navItems = [
    { label: 'Dashboard', href: '/dashboard' },
    { label: 'Players', href: '/dashboard?tab=players' },
    { label: 'Bans', href: '/dashboard?tab=bans' },
    { label: 'Logs', href: '/dashboard?tab=logs' },
    { label: 'Config', href: '/dashboard?tab=config' },
  ]

  function toggleMenu() {
    open = !open
  }

  function closeMenu() {
    open = false
  }
</script>

<nav class="border-b border-white/5 bg-sentinel-surface/50 backdrop-blur" aria-label="Main navigation">
  <div class="mx-auto flex max-w-7xl items-center justify-between px-4 py-3">
    <a href="/dashboard" class="text-lg font-bold tracking-tight touch-target" aria-label="Sentinel Home">
      Sentinel
    </a>

    <!-- Desktop nav -->
    <div class="hidden items-center gap-1 md:flex">
      {#each navItems as item}
        <a
          href={item.href}
          class="rounded-md px-3 py-2 text-sm font-medium text-sentinel-secondary transition hover:bg-white/5 hover:text-sentinel-primary touch-target"
        >
          {item.label}
        </a>
      {/each}
      <button
        onclick={onLogout}
        class="ml-2 rounded-md px-3 py-2 text-sm font-medium text-sentinel-danger transition hover:bg-white/5 touch-target"
        aria-label="Sign out"
      >
        Sign out
      </button>
    </div>

    <!-- Hamburger button -->
    <button
      class="touch-target rounded-md p-2 text-sentinel-secondary transition hover:bg-white/5 hover:text-sentinel-primary md:hidden"
      onclick={toggleMenu}
      aria-expanded={open}
      aria-controls="mobile-menu"
      aria-label={open ? 'Close menu' : 'Open menu'}
    >
      <svg class="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2" aria-hidden="true">
        {#if open}
          <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
        {:else}
          <path stroke-linecap="round" stroke-linejoin="round" d="M4 6h16M4 12h16M4 18h16" />
        {/if}
      </svg>
    </button>
  </div>

  <!-- Mobile menu -->
  {#if open}
    <div id="mobile-menu" class="border-t border-white/5 md:hidden">
      <div class="space-y-1 px-4 py-3">
        {#each navItems as item}
          <a
            href={item.href}
            class="block rounded-md px-3 py-2.5 text-base font-medium text-sentinel-secondary transition hover:bg-white/5 hover:text-sentinel-primary touch-target"
            onclick={closeMenu}
          >
            {item.label}
          </a>
        {/each}
        <button
          onclick={() => { closeMenu(); onLogout?.(); }}
          class="block w-full rounded-md px-3 py-2.5 text-left text-base font-medium text-sentinel-danger transition hover:bg-white/5 touch-target"
        >
          Sign out
        </button>
      </div>
    </div>
  {/if}
</nav>
