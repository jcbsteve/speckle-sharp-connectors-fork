(defun startup ()

  ;; Read environment variables
  (setq project (getenv "PLANT_PROJECT"))
  (setq mappings (getenv "PLANT_DRAWING_MAPPINGS"))
 
  (if (and project mappings)
    (progn
      (princ (strcat "\nProject=" project))
      (princ "\nOpening Plant project...\n")

      ;; Double-open workaround sometimes required
      (command "-openproject" project)
      (command "-openproject" project)

      ;; The batch command writes the result file and closes Plant 3D when it finishes.
      (command "-SPECKLEPUBLISH")
    )
    ;; Else print out the missing variables
    (progn
      (if (not project) (princ "\nERROR: PLANT_PROJECT environment variable is not set.\n"))
      (if (not mappings) (princ "\nERROR: PLANT_DRAWING_MAPPINGS environment variable is not set.\n"))
    )
  )
)

(defun c:STARTUP ()
  (startup)
)
