<script lang="ts">
  import { onMount } from 'svelte'
  import { getToken } from './lib/auth'
  import { getRoute, navigate, onRouteChange } from './lib/router'
  import Login from './routes/Login.svelte'
  import Dashboard from './routes/Dashboard.svelte'

  let route = $state(getRoute())
  let token = $state(getToken())

  onMount(() => {
    const unsub = onRouteChange((r) => {
      route = r
      token = getToken()
    })
    return unsub
  })

  // Redirect unauthenticated users to login
  $effect(() => {
    const isAuthed = !!token
    const isLogin = route === '/login' || route === '/'
    if (!isAuthed && !isLogin) {
      navigate('/login')
    }
  })

  function handleLoginSuccess() {
    token = getToken()
    navigate('/dashboard')
  }

  function handleLogout() {
    token = null
    navigate('/login')
  }
</script>

{#if route === '/login' || route === '/'}
  <Login onSuccess={handleLoginSuccess} />
{:else if route === '/dashboard'}
  <Dashboard {token} onLogout={handleLogout} />
{:else}
  <div class="flex min-h-screen items-center justify-center">
    <p class="text-sentinel-secondary">404 — Not Found</p>
  </div>
{/if}
