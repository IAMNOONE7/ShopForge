export function Message({ title, text }: { title: string; text: string }) {
  return (
    <section className="message">
      <h1>{title}</h1>
      <p>{text}</p>
    </section>
  )
}
