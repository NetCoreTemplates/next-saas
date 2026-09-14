import { ref, computed, onMounted } from "vue"
import { ApiResult, toDate } from "@servicestack/client"
import { useClient, useUtils, useFormatters, css } from "@servicestack/vue"
import { QueryUserApiKeys, CreateUserApiKey, UpdateUserApiKey, DeleteUserApiKey } from "./apikeys-apis.mjs"

function arraysAreEqual(a, b) {
    if (!a || !b) return false
    return a.length === b.length && a.every((v, i) => v === b[i])
}

const CopyIcon = {
    template:`
      <button type="button" @click="copy(text)" class="identity-api-key-copy" :aria-label="copied ? 'API key copied' : 'Copy API key'" :title="copied ? 'Copied' : 'Copy API key'">
        <svg v-if="copied" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path></svg>
        <svg v-else class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M9 8.25V6.5A2.5 2.5 0 0 1 11.5 4h6A2.5 2.5 0 0 1 20 6.5v6a2.5 2.5 0 0 1-2.5 2.5h-1.75M6.5 9h6A2.5 2.5 0 0 1 15 11.5v6a2.5 2.5 0 0 1-2.5 2.5h-6A2.5 2.5 0 0 1 4 17.5v-6A2.5 2.5 0 0 1 6.5 9Z"/></svg>
        <span>{{ copied ? 'Copied' : 'Copy' }}</span>
      </button>
    `,
    props:['text'],
    setup(props) {
        const { copyText } = useUtils()
        const copied = ref(false)

        function copy(text) {
            copied.value = true
            copyText(text)
            setTimeout(() => copied.value = false, 3000)
        }

        return { copied, copy, }
    }
}

const CreateApiKeyForm = {
    components: { CopyIcon },
    template:`
      <div>
        <ModalDialog v-if="apiKey" size-class="identity-api-key-dialog" @done="done">
          <div class="identity-api-key-modal">
            <div class="identity-api-key-header">
              <span class="identity-api-key-success" aria-hidden="true">
                <svg class="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"/></svg>
              </span>
              <div>
                <p class="identity-api-key-eyebrow">Credential created</p>
                <h3>Save your new API key</h3>
                <p class="identity-api-key-intro">Use this key to authenticate requests from your application.</p>
              </div>
            </div>
            <div class="identity-api-key-body">
              <label for="apikey">API key</label>
              <div class="identity-api-key-value">
                <input id="apikey" type="text" :value="apiKey" @focus="$event.target.select()" readonly autocomplete="off" spellcheck="false" />
                <CopyIcon :text="apiKey" />
              </div>
              <div class="identity-api-key-warning">
                <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 4.5h.008v.008H12V16.5Z"/></svg>
                <p><strong>Copy it now.</strong> For your security, Acme stores only a protected fingerprint and cannot show this key again.</p>
              </div>
            </div>
            <div class="identity-api-key-footer">
              <PrimaryButton @click="done" class="identity-api-key-confirm">I’ve saved this key</PrimaryButton>
            </div>
          </div>
        </ModalDialog>
        <form v-else @submit="submit" :class="[css.card.panelClass, 'identity-api-key-form']">
          <input type="submit" class="hidden">
          <div class="identity-api-key-form-main relative">
            <CloseButton class="sm:block" @close="$emit('done')" />
            <div>
              <div>
                <ErrorSummary v-if="errorSummary" class="mb-3" :errorSummary="errorSummary" />
                <div class="identity-api-key-form-heading">
                  <h3>Create API key</h3>
                  <p>Choose a recognizable name and an optional expiration date.</p>
                </div>
                <div class="identity-api-key-form-fields">
                  <fieldset>
                    <div class="grid grid-cols-6 gap-6">
                      <div class="col-span-6 sm:col-span-3">
                        <TextInput id="name" v-model="request.name" required placeholder="Name of this API Key" />
                      </div>
                      <div class="col-span-6 sm:col-span-3">
                        <SelectInput id="expiresIn" v-model="expiresIn" :entries="info.expiresIn" />
                      </div>
                      <div v-if="info.features.length" class="col-span-6">
                        <div class="mb-2">
                          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300">Features</label>
                        </div>
                        <div class="grid grid-cols-3 xl:grid-cols-4 gap-4">
                          <CheckboxInput v-for="feature in info.features" :id="feature" :label="feature" v-model="features[feature]" />
                        </div>
                      </div>
                      <div v-if="!info.hide.includes('Notes')" class="col-span-6">
                        <TextareaInput id="notes" v-model="request.notes" placeholder="Optional Notes about this API Key" class="h-24" />
                      </div>
                    </div>
                  </fieldset>
                </div>
              </div>
            </div>
          </div>
          <div class="identity-api-key-form-footer">
            <div>
              <SecondaryButton @click="$emit('done')">Cancel</SecondaryButton>
            </div>
            <div>
              <PrimaryButton>Create API Key</PrimaryButton>
            </div>
          </div>
        </form>
      </div>
    `,
    props: {
        refId: Number,
        refIdStr: String,
        info: Object,
    },
    emits:['done'],
    setup(props, { emit }) {
        const client = useClient()
        const request = ref(new CreateUserApiKey({
            refId: props.refId,
            refIdStr: props.refIdStr,
        }))
        const apiKey = ref('')
        const expiresIn = ref('')
        const features = ref({})
        const api = ref(new ApiResult())
        const errorSummary = computed(() => api.value.summaryMessage())
        
        async function submit(e) {
            e.preventDefault()
            if (expiresIn.value) {
                const days = parseInt(expiresIn.value)
                if (days > 0) {
                    const date = new Date()
                    date.setDate(date.getDate() + days)
                    request.value.expiryDate = date
                }
            }
            Object.keys(features.value).forEach(k => {
                if (features.value[k]) {
                    request.value.features ??= []
                    request.value.features.push(k)
                }
            })
            api.value = await client.api(request.value)
            apiKey.value = api.value.response?.result ?? ''
        }
        
        function done() {
            emit('done')
        }
        
        return { request, expiresIn, features, api, errorSummary, css, apiKey, done, submit }
    }
}

const EditApiKeyForm = {
    template:`
        <div>
          <form @submit="submit" :class="[css.card.panelClass, 'identity-api-key-form']">
            <input type="submit" class="hidden">
            <div class="identity-api-key-form-main relative">
              <CloseButton class="sm:block" @close="$emit('done')" />
              <div>
                <div>
                  <ErrorSummary v-if="errorSummary" class="mb-3" :errorSummary="errorSummary" />
                  <div class="identity-api-key-form-heading">
                    <h3>API key settings</h3>
                    <p>Update the key name, expiration date, or lifecycle status.</p>
                  </div>
                  <div class="identity-api-key-form-fields">
                    <fieldset>
                      <div class="grid grid-cols-6 gap-6">
                        <div class="col-span-6 sm:col-span-3">
                          <TextInput id="name" v-model="request.name" required placeholder="Name of this API Key" />
                        </div>
                        <div class="col-span-6 sm:col-span-3">
                          <TextInput id="expiryDate" type="date" v-model="request.expiryDate" />
                        </div>
                        <div v-if="info.features.length" class="col-span-6">
                          <div class="mb-2">
                            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300">Features</label>
                          </div>
                          <div class="grid grid-cols-3 xl:grid-cols-4 gap-4">
                            <CheckboxInput v-for="feature in info.features" :id="feature" :label="feature" v-model="features[feature]" />
                          </div>
                        </div>
                        <div v-if="!info.hide.includes('Notes')" class="col-span-6">
                          <TextareaInput id="notes" v-model="request.notes" placeholder="Optional Notes about this API Key" class="h-24" />
                        </div>
                      </div>
                      <div class="mt-2 col-span-6">
                        <div v-if="request.cancelledDate" class="flex items-center">
                            <div class="text-red-500">Disabled on {{formatDate(request.cancelledDate)}}</div>
                            <SecondaryButton @click="submitEnable" class="ml-4">Enable API Key</SecondaryButton>
                        </div>
                        <PrimaryButton v-else @click="submitDisable" color="red" class="mr-2">Disable API Key</PrimaryButton>
                      </div>
                    </fieldset>
                  </div>
                </div>
              </div>
            </div>
            <div class="identity-api-key-form-footer">
              <div>
                <ConfirmDelete @delete="submitDelete" />
              </div>
              <div>
                <SecondaryButton @click="$emit('done')">Close</SecondaryButton>
                <PrimaryButton class="ml-2">Save Changes</PrimaryButton>
              </div>
            </div>
          </form>
        </div>
    `,
    emits:['done'],
    props: {
        id: Number,
        info: Object,
    },
    setup(props, { emit }) {
        const client = useClient()
        const { dateInputFormat } = useUtils()
        const { formatDate } = useFormatters()

        let origValues = {}
        const request = ref(new UpdateUserApiKey())
        const features = ref({})
        const api = ref(new ApiResult())
        const errorSummary = computed(() => api.value.summaryMessage())

        async function submit(e) {
            e.preventDefault()
            
            const update = new UpdateUserApiKey({ id: props.id })
            
            request.value.features = []
            Object.keys(features.value).forEach(k => {
                if (features.value[k]) {
                    request.value.features.push(k)
                }
            })

            ;['name','expiryDate','features','notes'].forEach(k => {
                const value = request.value[k]
                const origValue = origValues[k]
                console.log(k, value, origValue, Array.isArray(value) ? arraysAreEqual(value, origValue) : -1)
                if (value === origValue) return
                if (Array.isArray(value)) {
                    if (!origValue || !arraysAreEqual(value, origValue)) {
                        if (value.length === 0) {
                            update.reset ??= []
                            update.reset.push(k)
                        } else {
                            update[k] = value
                        }
                    }
                }
                else if (value) {
                    update[k] = value
                } else {
                    update.reset ??= []
                    update.reset.push(k)
                }
            })

            api.value = await client.api(update)
            done()
        }

        function done() {
            emit('done')
        }

        async function submitDelete() {
            const apiDelete = await client.api(new DeleteUserApiKey({ id: props.id }))
            done()
        }

        async function submitDisable() {
            const apiDelete = await client.api(new UpdateUserApiKey({
                id: props.id,
                cancelledDate: new Date()
            }))
            done()
        }

        async function submitEnable() {
            const apiDelete = await client.api(new UpdateUserApiKey({
                id: props.id,
                reset: ['cancelledDate']
            }))
            done()
        }
        
        onMounted(async () => {
            const apiQuery = await client.api(new QueryUserApiKeys({ id: props.id }))
            if (apiQuery.succeeded && apiQuery.response.results.length === 1) {
                const result = apiQuery.response.results[0]
                request.value = new UpdateUserApiKey(result)
                for (const feature of request.value.features) {
                    features.value[feature] = true
                }
                request.value.expiryDate = request.value.expiryDate
                    ? dateInputFormat(toDate(request.value.expiryDate))
                    : null
                origValues = { ...result }
            }
        })
        
        return { css, request, features, errorSummary, formatDate,
            submit, submitDelete, submitDisable, submitEnable }
    }
}

const ManageUserApiKeys = {
    components: {
        CreateApiKeyForm,
        EditApiKeyForm,
    },
    template:`
        <div class="identity-api-key-toolbar">
          <div>
            <h2>Programmatic access</h2>
            <p>Create and manage credentials used by your applications.</p>
          </div>
          <PrimaryButton @click="toggleDialog('CreateApiKeyForm')" class="identity-api-key-new">
            {{ show === 'CreateApiKeyForm' ? 'Close' : 'New API key' }}
          </PrimaryButton>
        </div>
        <div>
          <CreateApiKeyForm v-if="show==='CreateApiKeyForm'" :info="info" :ref-id-str="workspaceId" @done="done" class="mt-2" :key="renderKey" />
          <EditApiKeyForm v-else-if="selected" :info="info" :id="selected" @done="done" class="mt-2" :key="renderKey+1" />
        </div>
        <div v-if="loading" class="identity-api-key-loading" aria-live="polite">
          <span></span><span></span><span></span>
        </div>
        <div v-else-if="api.response?.results?.length" class="identity-api-key-list">
          <div class="identity-api-key-table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Key</th>
                  <th>Created</th>
                  <th>Expires</th>
                  <th>Last used</th>
                  <th>Status</th>
                  <th><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in api.response.results" :key="row.id"
                    :class="{ 'is-selected': selected === row.id, 'is-disabled': !row.active }"
                    @click="rowSelected(row)">
                  <td>
                    <div class="identity-api-key-name">
                      <span class="identity-api-key-name-icon" aria-hidden="true">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="M15.75 5.25a4.5 4.5 0 1 1-6.44 4.06L3.75 14.87v2.38h2.38v2.38H8.5v-2.38h2.38l1.87-1.87"/></svg>
                      </span>
                      <span>
                        <strong>{{ row.name || 'Unnamed key' }}</strong>
                        <code>{{ row.visibleKey }}</code>
                      </span>
                    </div>
                  </td>
                  <td>{{ formatDate(row.createdDate) }}</td>
                  <td>{{ row.expiryDate ? formatDate(row.expiryDate) : 'Never' }}</td>
                  <td>
                    <span v-if="row.lastUsedDate" :title="formatDate(row.lastUsedDate)">{{ relativeTime(row.lastUsedDate) }}</span>
                    <span v-else class="identity-api-key-muted">Not used yet</span>
                  </td>
                  <td>
                    <span :class="['identity-api-key-status', row.active ? 'is-active' : 'is-inactive']">
                      <span aria-hidden="true"></span>{{ row.active ? 'Active' : 'Disabled' }}
                    </span>
                  </td>
                  <td class="identity-api-key-action-cell">
                    <button type="button" class="identity-api-key-edit" @click.stop="rowSelected(row)" :aria-label="'Edit ' + (row.name || 'API key')" title="Edit API key">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.8" d="m9 18 6-6-6-6"/></svg>
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div v-else class="identity-api-key-empty">
          <span class="identity-api-key-empty-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.6" d="M15.75 5.25a4.5 4.5 0 1 1-6.44 4.06L3.75 14.87v2.38h2.38v2.38H8.5v-2.38h2.38l1.87-1.87"/></svg>
          </span>
          <h3>No API keys yet</h3>
          <p>Create a key when you’re ready to connect your first application.</p>
        </div>
    `,
    props: {
        user: Object,
        columns: Array,
        info: Object,
        workspaceId: String,
    },
    setup(props) {

        const { formatDate, relativeTime } = useFormatters()
        const renderKey = ref(0)
        const api = new ref(new ApiResult())
        const client = useClient()
        const show = ref('')
        const selected = ref()
        const loading = ref(true)
        
        async function refresh() {
            loading.value = true
            try {
                const request = new QueryUserApiKeys({ orderBy:'-id' })
                api.value = await client.api(request)
                if (api.value.response?.results) {
                    api.value.response.results = api.value.response.results.filter(x => x.refIdStr === props.workspaceId)
                }
            } finally {
                loading.value = false
            }
        }
        
        async function done() {
            show.value = ''
            selected.value = null
            await refresh()
        }
        
        function toggleDialog(dialog) {
            show.value = show.value === dialog ? '' : dialog
        }
        
        function rowSelected(row) {
            show.value = ''
            selected.value = selected.value === row.id ? null : row.id
            renderKey.value++
        }
        
        onMounted(async () => {
            await refresh()
        })
        
        return { renderKey, show, api, loading, toggleDialog, done, formatDate, relativeTime, selected, rowSelected }
    }
}

function install(app) {
    app.components({ CreateApiKeyForm, EditApiKeyForm })
}

export default ManageUserApiKeys
