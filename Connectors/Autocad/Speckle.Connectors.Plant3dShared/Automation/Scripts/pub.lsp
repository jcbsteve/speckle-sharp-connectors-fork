(defun startup ()

  ;; Read environment variables
  (setq project (getenv "PLANT_PROJECT"))
  (setq mappings (getenv "PLANT_DRAWING_MAPPINGS"))
  ;; pub.ps1 sets -SPECKLELIST to only write the drawing inventory.
  (setq batchCommand (getenv "PLANT_BATCH_COMMAND"))
  (if (not batchCommand) (setq batchCommand "-SPECKLEPUBLISH"))

  (if (and project mappings)
    (progn
      (princ (strcat "\nProject=" project))
      (princ "\nOpening Plant project...\n")

      ;; Double-open workaround sometimes required
      (command "-openproject" project)
      (command "-openproject" project)

      ;; The batch command writes its output file and closes Plant 3D when it finishes.
      (command batchCommand)
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
