import { useId, useState, type InputHTMLAttributes } from 'react'
import { Eye, EyeOff } from 'lucide-react'

type PasswordFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'id'> & {
  label: string
  hint?: string
}

export function PasswordField({ label, hint, ...inputProps }: PasswordFieldProps) {
  const id = useId()
  const [visible, setVisible] = useState(false)
  return <div className="field">
    <label htmlFor={id}>{label}</label>
    <div className="password-input">
      <input {...inputProps} id={id} type={visible ? 'text' : 'password'} />
      <button type="button" className="password-input__toggle" onClick={() => setVisible((value) => !value)}
        aria-label={visible ? `Hide ${label.toLowerCase()}` : `Show ${label.toLowerCase()}`}
        title={visible ? 'Hide password' : 'Show password'}>
        {visible ? <EyeOff size={18} /> : <Eye size={18} />}
      </button>
    </div>
    {hint && <small>{hint}</small>}
  </div>
}
