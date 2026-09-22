import { api, type FailedMessage } from '../api'
import { useAction } from '../useAction'
import { useRequest } from '../useRequest'

export function FailedMessagesSection({ storeId, culture }: { storeId: string; culture: string }) {
  const [messages, reload] = useRequest(`failed-messages:${storeId}`, () => api.failedMessages(storeId))
  const [error, run] = useAction(reload)
  const failed: FailedMessage[] = messages.status === 'ready' ? messages.data : []

  return (
    <section>
      <h2>Background messages</h2>
      <p className="hint">Work that runs after a request — confirmation e-mails, notifications. Anything here was retried and gave up.</p>
      {error && <p className="error">{error}</p>}
      {messages.status === 'ready' && failed.length === 0 && <p className="hint">Nothing failed.</p>}
      {failed.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Message</th>
              <th>Created</th>
              <th>Attempts</th>
              <th>Last error</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {failed.map((message) => (
              <tr key={message.id}>
                <td>{message.type}</td>
                <td>{new Date(message.createdAt).toLocaleString(culture)}</td>
                <td>{message.attempts}</td>
                <td>{message.error}</td>
                <td>
                  <button type="button" onClick={() => run(() => api.requeueMessage(storeId, message.id))}>
                    Try again
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
