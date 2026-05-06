<script lang="ts">
  import { setToken, validateToken } from '../lib/auth'

  interface Props {
    onSuccess?: () => void
  }

  let { onSuccess }: Props = $props()

  let tokenInput = $state('')
  let error = $state('')
  let loading = $state(false)

  async function handleSubmit(e: Event) {
    e.preventDefault()
    error = ''
    loading = true

    const token = tokenInput.trim()
    if (!token) {
      error = 'Please enter an access token'
      loading = false
      return
    }

    const isValid = await validateToken(token)
    if (isValid) {
      setToken(token)
      onSuccess?.()
    } else {
      error = 'Invalid token'
    }
    loading = false
  }
</script>

<div class="flex min-h-screen flex-col items-center justify-center px-4">
  <div class="w-full max-w-sm space-y-6">
    <div class="text-center">
      <h1 class="text-2xl font-bold tracking-tight">Sentinel</h1>
      <p class="mt-1 text-sm text-sentinel-secondary">Admin Access Panel</p>
    </div>

    <form onsubmit={handleSubmit} class="space-y-4" aria-label="Login form">
      <div>
        <label for="token" class="mb-1 block text-sm font-medium">Access Token</label>
        <input
          id="token"
          type="password"
          bind:value={tokenInput}
          placeholder="Enter your access token"
          class="w-full rounded-md border border-white/10 bg-sentinel-surface px-3 py-2.5 text-sm text-sentinel-primary placeholder:text-sentinel-secondary/50 focus:border-sentinel-accent focus:outline-none focus:ring-1 focus:ring-sentinel-accent touch-target"
          autocomplete="off"
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={error ? 'token-error' : undefined}
        />
      </div>

      {#if error}
        <p id="token-error" class="text-sm font-medium text-sentinel-danger" role="alert" aria-live="assertive">
          {error}
        </p>
      {/if}

      <button
        type="submit"
        disabled={loading}
        class="w-full rounded-md bg-sentinel-accent px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-sentinel-accent focus:ring-offset-2 focus:ring-offset-sentinel-bg disabled:opacity-50 touch-target"
        aria-label="Sign In"
      >
        {loading ? 'Verifying…' : 'Sign In'}
      </button>
    </form>
  </div>
</div>
